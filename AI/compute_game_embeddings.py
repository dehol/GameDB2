"""
compute_game_embeddings.py
Обчислює embeddings для всіх ігор у GameDB та зберігає їх батчами.
"""

import json
import psycopg2
from psycopg2.extras import execute_values
from sentence_transformers import SentenceTransformer
from tqdm import tqdm
import logging

# ========================= НАЛАШТУВАННЯ =========================
DB_CONFIG = {
    "host": "localhost",
    "port": 5432,
    "database": "mygamedb",
    "user": "postgres",
    "password": "postgres",
}

MODEL_NAME = "BAAI/bge-base-en-v1.5"
BATCH_SIZE = 32       # скільки текстів за один encode()
SAVE_EVERY = 10       # зберігати в БД кожні N батчів (~320 ігор)
MAX_TEXT_LENGTH = 512

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)s | %(message)s"
)
log = logging.getLogger(__name__)


# ========================= DB =========================

def get_connection():
    return psycopg2.connect(**DB_CONFIG)


def fetch_games(conn) -> list[tuple]:
    """
    Повертає лише ігри, у яких ще немає ембедингу (EmbeddingUpdatedAt IS NULL).
    Якщо потрібно перерахувати всі — прибери умову AND g."EmbeddingUpdatedAt" IS NULL.
    """
    query = """
        SELECT
            g."GameId",
            g."Name",
            g."Description",
            ARRAY_AGG(DISTINCT gen."Name") FILTER (WHERE gen."Name" IS NOT NULL) AS genres,
            ARRAY_AGG(DISTINCT t."Name")   FILTER (WHERE t."Name" IS NOT NULL)   AS tags
        FROM "Game" g
        LEFT JOIN "GameGenre" gg  ON g."GameId" = gg."GameId"
        LEFT JOIN "Genre"     gen ON gg."GenreId" = gen."GenreId"
        LEFT JOIN "GameTag"   gt  ON g."GameId"  = gt."GameId"
        LEFT JOIN "Tag"       t   ON gt."TagId"  = t."TagId"
        WHERE g."ImportStatus" = 'Full'
        GROUP BY g."GameId", g."Name", g."Description"
        ORDER BY g."GameId";
    """
    with conn.cursor() as cur:
        cur.execute(query)
        return cur.fetchall()


def save_batch(conn, game_ids_batch: list, embeddings_batch: list) -> None:
    """
    Зберігає батч ембедингів у БД.
    Виправлення: використовуємо ::text замість ::jsonb,
    бо pgvector вміє кастити text → vector, але не jsonb → vector.
    """
    update_query = """
        UPDATE "Game"
        SET "Embedding" = data.embedding::vector
        FROM (VALUES %s) AS data(game_id, embedding)
        WHERE "Game"."GameId" = data.game_id;
    """
    values = [
        (gid, json.dumps(emb))
        for gid, emb in zip(game_ids_batch, embeddings_batch)
    ]
    with conn.cursor() as cur:
        execute_values(cur, update_query, values, template="(%s, %s::text)")
    conn.commit()


# ========================= TEXT BUILDER =========================

def build_game_text(game: dict) -> str:
    """Безпечне формування тексту для ембедінгу."""
    parts = []

    if game.get("name"):
        parts.append(str(game["name"]).strip())

    if game.get("description"):
        desc = str(game["description"])[:MAX_TEXT_LENGTH].strip()
        if desc:
            parts.append(desc)

    genres = game.get("genres") or []
    genres = [str(g).strip() for g in genres if g and str(g).strip()]
    if genres:
        parts.append("Genres: " + ", ".join(genres))

    tags = game.get("tags") or []
    tags = [str(t).strip() for t in tags if t and str(t).strip()]
    if tags:
        parts.append("Tags: " + ", ".join(tags[:12]))

    return "\n".join(parts).strip()


# ========================= MAIN =========================

def main():
    log.info("Завантаження моделі: %s", MODEL_NAME)
    model = SentenceTransformer(MODEL_NAME)
    dim = model.get_embedding_dimension()
    log.info("Модель завантажена ✓ (dim=%d)", dim)

    conn = get_connection()

    # ---------- Завантаження ігор ----------
    log.info("Завантаження ігор із БД (тільки без ембедингу)...")
    rows = fetch_games(conn)
    log.info("Знайдено %d ігор для обробки", len(rows))

    if not rows:
        log.warning("Немає ігор для обробки. Завершення.")
        return

    # ---------- Підготовка текстів ----------
    game_ids: list[int] = []
    texts: list[str] = []

    for row in tqdm(rows, desc="Підготовка текстів"):
        game_id = row[0]
        text = build_game_text({
            "name":        row[1],
            "description": row[2],
            "genres":      row[3] or [],
            "tags":        row[4] or [],
        })
        if text and len(text) > 15:
            game_ids.append(game_id)
            texts.append(text)

    log.info("Підготовлено %d текстів для ембедінгу", len(texts))

    if not texts:
        log.warning("Немає текстів для обробки. Завершення.")
        return

    # ---------- Обчислення + батчове збереження ----------
    pending_ids:  list[int]   = []
    pending_embs: list[list]  = []
    saved_total = 0

    total_batches = (len(texts) + BATCH_SIZE - 1) // BATCH_SIZE

    for batch_num, i in enumerate(
        tqdm(range(0, len(texts), BATCH_SIZE), desc="Обчислення embeddings"),
        start=1,
    ):
        batch_texts = texts[i : i + BATCH_SIZE]
        batch_ids   = game_ids[i : i + BATCH_SIZE]

        batch_emb = model.encode(
            batch_texts,
            batch_size=BATCH_SIZE,
            normalize_embeddings=True,
            show_progress_bar=False,
        )

        pending_ids.extend(batch_ids)
        pending_embs.extend(batch_emb.tolist())

        # Зберігаємо кожні SAVE_EVERY батчів або в кінці
        is_last_batch = (batch_num == total_batches)
        if len(pending_ids) >= BATCH_SIZE * SAVE_EVERY or is_last_batch:
            save_batch(conn, pending_ids, pending_embs)
            saved_total += len(pending_ids)
            log.info(
                "  → Збережено %d / %d (%.1f%%)",
                saved_total, len(texts),
                saved_total / len(texts) * 100,
            )
            pending_ids  = []
            pending_embs = []

    log.info("✅ Успішно збережено %d embeddings!", saved_total)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        log.error("Критична помилка:", exc_info=True)
    finally:
        print("\nГотово!")