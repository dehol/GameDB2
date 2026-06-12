"""
bot_v4.py — GameDB LangGraph Agent v4
======================================

АРХІТЕКТУРНІ ЗМІНИ відносно v3:

  ВИДАЛЕНО:
    - grader_node      (LLM-оцінка якості рекомендацій)
    - rewrite_node     (LLM-переписування запиту при невдачі)
    - all-MiniLM-L6-v2 (замінено на BAAI/bge-base-en-v1.5)
    - LLM-ранжування та оцінка результатів

  ДОДАНО:
    - intent_node      — LLM витягує структурований intent
    - profile_node     — API-виклик профілю користувача (без LLM)
    - planning_node    — LLM будує параметри пошуку (НЕ список ігор)
    - recommendation_node — виключно API, без LLM
    - explanation_node — LLM пояснює ЧОМУ ці ігри (після отримання результатів)
    - response_node    — форматування фінальної відповіді
    - tools_agent_node — збережений agentic підхід для ціни/алертів/etc.

НОВИЙ ГРАФ:

  START
    ↓
  intent_node  (LLM: витягує intent, genres, tags, price)
    ↓
  [router: intent == "recommendation"?]
    ├─ YES → profile_node → planning_node → recommendation_node
    │                                          ↓
    │                                       explanation_node
    │                                          ↓
    │                                       response_node → END
    └─ NO  → tools_agent_node ←→ safe_tools / sensitive_tools
                                              ↓ (HITL: set_price_alert)
                                             END

LLM РОБИТЬ:
  1. intent_node    — парсить запит у структурований JSON
  2. planning_node  — будує параметри пошуку (НЕ ранжує ігри)
  3. explanation_node — пояснює результати (НЕ обирає кращі)
  4. tools_agent_node — відповідає на питання про ціни/алерти

LLM НЕ РОБИТЬ:
  - ранжування ігор
  - оцінку релевантності
  - побудову recommendation score
  - вибір "кращих" результатів

Залежності:
  pip install langgraph langchain-groq langchain-core requests python-dotenv
              sentence-transformers
"""

from __future__ import annotations

import json
import logging
import os
import time
from typing import Annotated, Literal

import requests
from dotenv import load_dotenv
from langchain_core.messages import (
    AIMessage,
    BaseMessage,
    HumanMessage,
    SystemMessage,
    ToolMessage,
)
from langchain_core.runnables import RunnableConfig
from langchain_core.tools import tool
from langchain_groq import ChatGroq
from langgraph.checkpoint.memory import MemorySaver
from langgraph.graph import END, START, StateGraph
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode
from sentence_transformers import SentenceTransformer
from typing_extensions import TypedDict

load_dotenv()


# ══════════════════════════════════════════════════════════════════════════════
# КОНФІГУРАЦІЯ
# ══════════════════════════════════════════════════════════════════════════════

BOT_API_KEY:  str = os.getenv("GAMEDB_BOT_KEY", "")
GROQ_API_KEY: str = os.getenv("GROQ_API", "")
BOT_API_BASE: str = os.getenv("GAMEDB_API_URL", "http://localhost:5295")
MODEL_NAME:   str = os.getenv("MODEL_NAME", "meta-llama/llama-4-scout-17b-16e-instruct")

# Нова embedding model: BAAI/bge-base-en-v1.5
# Перевершує all-MiniLM-L6-v2 на BEIR benchmark, dim=768
EMBED_MODEL_NAME: str = os.getenv(
    "EMBED_MODEL", "BAAI/bge-base-en-v1.5"
)


# ══════════════════════════════════════════════════════════════════════════════
# ЛОГУВАННЯ
# ══════════════════════════════════════════════════════════════════════════════

def _setup_logging() -> None:
    fmt = logging.Formatter(
        fmt="%(asctime)s [%(levelname)-5s] %(name)-20s │ %(message)s",
        datefmt="%H:%M:%S",
    )
    handler = logging.StreamHandler()
    handler.setFormatter(fmt)
    root = logging.getLogger("gamedb")
    if not root.handlers:
        root.setLevel(logging.DEBUG)
        root.addHandler(handler)
    root.propagate = False


_setup_logging()
log_api    = logging.getLogger("gamedb.api")
log_nodes  = logging.getLogger("gamedb.nodes")
log_graph  = logging.getLogger("gamedb.graph")
log_hitl   = logging.getLogger("gamedb.hitl")


# ══════════════════════════════════════════════════════════════════════════════
# HTTP-ХЕЛПЕРИ
# ══════════════════════════════════════════════════════════════════════════════

_HEADERS = {"X-Bot-Api-Key": BOT_API_KEY}


def _get(path: str, **params) -> dict | list | None:
    url   = f"{BOT_API_BASE}/api/bot{path}"
    clean = {k: v for k, v in params.items() if v is not None}
    t0    = time.perf_counter()
    try:
        resp = requests.get(url, params=clean, headers=_HEADERS, timeout=15)
        ms   = (time.perf_counter() - t0) * 1000
        resp.raise_for_status()
        data = resp.json()
        n    = len(data.get("items", data) if isinstance(data, dict) else data)
        log_api.debug("GET  %-40s %5.0f ms  [%s items]", path, ms, n)
        return data
    except requests.RequestException as exc:
        ms = (time.perf_counter() - t0) * 1000
        log_api.error("GET  %-40s %5.0f ms  ПОМИЛКА: %s", path, ms, exc)
        return None


def _post(path: str, payload: dict) -> dict | list | None:
    url = f"{BOT_API_BASE}/api/bot{path}"
    t0  = time.perf_counter()
    try:
        resp = requests.post(url, json=payload, headers=_HEADERS, timeout=15)
        ms   = (time.perf_counter() - t0) * 1000
        resp.raise_for_status()
        data = resp.json()
        n    = len(data) if isinstance(data, list) else "–"
        log_api.debug("POST %-40s %5.0f ms  [%s items]", path, ms, n)
        return data
    except requests.RequestException as exc:
        ms = (time.perf_counter() - t0) * 1000
        log_api.error("POST %-40s %5.0f ms  ПОМИЛКА: %s", path, ms, exc)
        return None


def _parse_json_response(raw: str, fallback: dict | list) -> dict | list:
    """Безпечний парсинг JSON з LLM (прибирає markdown-огорожі)."""
    cleaned = (
        raw.strip()
        .removeprefix("```json")
        .removeprefix("```")
        .removesuffix("```")
        .strip()
    )
    try:
        return json.loads(cleaned)
    except (json.JSONDecodeError, ValueError):
        log_graph.warning("_parse_json_response: невалідний JSON → fallback")
        return fallback


# ══════════════════════════════════════════════════════════════════════════════
# EMBEDDING MODEL
# ══════════════════════════════════════════════════════════════════════════════

log_graph.info("Завантаження SentenceTransformer (%s)…", EMBED_MODEL_NAME)
_embed_model = SentenceTransformer(EMBED_MODEL_NAME)
log_graph.info("Embedding-модель готова ✓  dim=%d", _embed_model.get_sentence_embedding_dimension())


# ══════════════════════════════════════════════════════════════════════════════
# AGENT STATE
# ══════════════════════════════════════════════════════════════════════════════

class AgentState(TypedDict):
    # Конверсаційні повідомлення (використовуються tools_agent_node та response_node)
    messages: Annotated[list[BaseMessage], add_messages]

    # Ідентифікатор користувача
    user_id: int

    # Оригінальний запит (для контексту в explanation_node)
    original_query: str

    # ── Intent extraction ──────────────────────────────────────────────────
    intent: str                # "recommendation" | "price_check" | "set_alert" | "general"
    genres: list[str]          # жанри з запиту
    tags: list[str]            # теги з запиту
    exclude_genres: list[str]
    exclude_tags: list[str]
    required_genres: list[str]  # обов'язкові жанри (явно вказані)
    required_tags: list[str]
    max_price: float | None
    is_free: bool | None
    reference_games: list[str]  # назви референсних ігор

    # ── Pipeline data ──────────────────────────────────────────────────────
    user_profile: dict | None        # з GET /api/bot/users/{id}/profile
    search_plan: dict | None         # з planning_node
    recommendation_results: list     # з recommendation_node
    recommendation_reasoning: str    # пояснення плану пошуку (для explanation_node)


# ══════════════════════════════════════════════════════════════════════════════
# LLM
# ══════════════════════════════════════════════════════════════════════════════

llm = ChatGroq(
    groq_api_key=GROQ_API_KEY,
    model_name=MODEL_NAME,
    temperature=0,
    model_kwargs={"parallel_tool_calls": False},
)


# ══════════════════════════════════════════════════════════════════════════════
# TOOLS (для tools_agent_node: ціни, алерти, пошук назв)
# ══════════════════════════════════════════════════════════════════════════════

def _coerce_price(v) -> float | None:
    if v is None: return None
    try: return float(str(v).strip())
    except ValueError: return None


def _coerce_bool(v) -> bool | None:
    if v is None: return None
    if isinstance(v, bool): return v
    return str(v).strip().lower() in ("true", "1", "yes")


@tool
def get_current_offers(game_id: int | str) -> str:
    """
    Повертає актуальні ціни та знижки для гри за GameId.
    Містить: назву, валюту, поточний мінімум, базову ціну, магазини.
    """
    game_id = int(game_id)
    data = _get(f"/game/{game_id}/price-context")
    if not data:
        return json.dumps({"error": f"Не вдалося отримати ціни для GameId {game_id}"})
    return json.dumps({
        "GameName":      data.get("gameName"),
        "Currency":      data.get("currency"),
        "CurrentLowest": data.get("currentLowest"),
        "BasePrice":     data.get("basePrice"),
        "Shops":         data.get("shops", []),
        "ExistingAlert": data.get("existingAlert"),
    }, ensure_ascii=False)


@tool
def get_price_dynamics(game_id: int | str) -> str:
    """
    Повертає historical minimum ціни (найнижча за весь час).
    Використовуй разом з get_current_offers для оцінки вигідності поточної ціни.
    """
    game_id = int(game_id)
    data = _get(f"/game/{game_id}/price-context")
    if not data:
        return json.dumps({"HistoricalLow": None})
    return json.dumps({
        "GameName":      data.get("gameName"),
        "HistoricalLow": data.get("historicalLow"),
        "CurrentLowest": data.get("currentLowest"),
        "BasePrice":     data.get("basePrice"),
        "Currency":      data.get("currency"),
    }, ensure_ascii=False)


@tool
def set_price_alert(
    game_id: int | str,
    target_price: float | str,
    config: RunnableConfig,
) -> str:
    """
    ВСТАНОВЛЮЄ ціновий алерт для поточного користувача.
    ВИКЛИЧ ТІЛЬКИ після явного підтвердження (слово 'так', 'yes', 'ок').
    """
    game_id      = int(game_id)
    target_price = float(target_price)
    user_id      = config["configurable"]["user_id"]
    data = _post("/alerts", {
        "userId": user_id, "gameId": game_id, "targetPrice": target_price
    })
    if data and data.get("success"):
        return f"Success: {data.get('message', f'Алерт встановлено для GameId {game_id} на {target_price}.')}"
    return f"Error: {(data or {}).get('error', 'API недоступний')}"


@tool
def resolve_game_name(query: str) -> str:
    """
    Розпізнає повну офіційну назву гри за скороченням або неточною назвою.
    Виклич ТІЛЬКИ якщо query є скороченням реальної гри (GTA5, RDR2, DS3, BOTW).
    ЗАБОРОНЕНО: НЕ викликай для описів стилю, жанру або сеттінгу.
    """
    try:
        resp = requests.post(
            "https://api.groq.com/openai/v1/chat/completions",
            headers={
                "Authorization": f"Bearer {GROQ_API_KEY}",
                "Content-Type": "application/json",
            },
            json={
                "model": "llama-3.3-70b-versatile",
                "messages": [
                    {
                        "role": "system",
                        "content": "You are a video game database expert. "
                                   "Return ONLY a valid JSON array of strings, "
                                   "no markdown, no explanation.",
                    },
                    {
                        "role": "user",
                        "content": (
                            f'The user is searching for a game using: "{query}"\n\n'
                            "List up to 5 possible full official game titles, "
                            "ordered by likelihood.\n"
                            'Examples: "GTA5"→["Grand Theft Auto V"], '
                            '"DS3"→["Dark Souls III"]\n\n'
                            "Return ONLY the JSON array."
                        ),
                    },
                ],
                "temperature": 0,
                "max_tokens": 200,
            },
            timeout=15,
        )
        resp.raise_for_status()
        raw = resp.json()["choices"][0]["message"]["content"].strip()
        return json.dumps(_parse_json_response(raw, [query]), ensure_ascii=False)
    except Exception as exc:
        log_graph.error("resolve_game_name: %s", exc)
        return json.dumps([query])


@tool
def get_user_library(config: RunnableConfig) -> str:
    """
    Повертає OwnedGameIds та WishlistGameIds поточного користувача.
    Використовуй для відповіді на питання про бібліотеку/вішліст.
    """
    user_id = config["configurable"]["user_id"]
    data = _get(f"/library/{user_id}")
    result = data or {"ownedGameIds": [], "wishlistGameIds": []}
    return json.dumps(result, ensure_ascii=False)


# ── Tool lists ────────────────────────────────────────────────────────────────

_utility_safe_tools      = [get_current_offers, get_price_dynamics, resolve_game_name, get_user_library]
_utility_sensitive_tools = [set_price_alert]

utility_safe_node      = ToolNode(_utility_safe_tools)
utility_sensitive_node = ToolNode(_utility_sensitive_tools)

llm_with_utility_tools = llm.bind_tools(_utility_safe_tools + _utility_sensitive_tools)


# ══════════════════════════════════════════════════════════════════════════════
# METADATA для system_prompt
# ══════════════════════════════════════════════════════════════════════════════

def _load_metadata() -> tuple[list[str], list[str]]:
    meta   = _get("/metadata") or {}
    genres = meta.get("genres", [])
    tags   = meta.get("tags", [])
    log_graph.info("Метадані: %d жанрів, %d тегів", len(genres), len(tags))
    return genres, tags


_CATALOG_GENRES, _CATALOG_TAGS = _load_metadata()
_GENRES_STR = ", ".join(_CATALOG_GENRES[:60]) or "недоступно"
_TAGS_STR   = ", ".join(_CATALOG_TAGS[:100])  or "недоступно"


# ══════════════════════════════════════════════════════════════════════════════
# NODE 1: intent_node
# LLM витягує структурований intent з запиту користувача.
# Не повертає ігри, не ранжує — лише парсить запит.
# ══════════════════════════════════════════════════════════════════════════════

_INTENT_SYSTEM = f"""Ти — парсер запитів для системи рекомендацій ігор.
Витягни структуровану інформацію з повідомлення користувача.

Доступні жанри: {_GENRES_STR}
Доступні теги (зразок): {_TAGS_STR[:500]}

Правила:
- intent: "recommendation" якщо просить порадити/рекомендувати ігри
- intent: "price_check" якщо питає про ціну конкретної гри
- intent: "set_alert" якщо хоче встановити алерт на ціну
- intent: "general" для інших питань
- genres/tags — ТІЛЬКИ з наявних у каталозі (наведено вище), АНГЛІЙСЬКОЮ
- required_genres — жанри які ОБОВ'ЯЗКОВО мають бути (явно вказані: "хочу RPG")
- exclude_genres/exclude_tags — небажані характеристики
- reference_games — конкретні назви ігор як приклад ("як Witcher 3")
- max_price — число або null (НЕ рядок)
- is_free — true/false або null (НЕ рядок)

Повертай ТІЛЬКИ валідний JSON, без markdown, без пояснень."""

_INTENT_USER_TEMPLATE = """Запит: "{query}"

Поверни JSON:
{{
  "intent": "recommendation|price_check|set_alert|general",
  "genres": [],
  "tags": [],
  "required_genres": [],
  "required_tags": [],
  "exclude_genres": [],
  "exclude_tags": [],
  "max_price": null,
  "is_free": null,
  "reference_games": []
}}"""


def intent_node(state: AgentState) -> dict:
    """
    Крок 1: LLM витягує structured intent з запиту.
    Результат зберігається в state, НЕ у messages.
    """
    query = state.get("original_query", "")
    log_nodes.info("intent_node → query=%r", query[:80])

    prompt = _INTENT_USER_TEMPLATE.format(query=query)
    raw    = llm.invoke([
        SystemMessage(content=_INTENT_SYSTEM),
        HumanMessage(content=prompt),
    ]).content

    data = _parse_json_response(raw, {
        "intent": "recommendation",
        "genres": [], "tags": [],
        "required_genres": [], "required_tags": [],
        "exclude_genres": [], "exclude_tags": [],
        "max_price": None, "is_free": None,
        "reference_games": [],
    })

    if not isinstance(data, dict):
        data = {"intent": "recommendation", "genres": [], "tags": [],
                "required_genres": [], "required_tags": [],
                "exclude_genres": [], "exclude_tags": [],
                "max_price": None, "is_free": None, "reference_games": []}

    intent = data.get("intent", "recommendation")
    log_nodes.info(
        "intent_node ← intent=%r genres=%s reqGenres=%s",
        intent, data.get("genres"), data.get("required_genres"),
    )

    return {
        "intent":          intent,
        "genres":          data.get("genres", []),
        "tags":            data.get("tags", []),
        "required_genres": data.get("required_genres", []),
        "required_tags":   data.get("required_tags", []),
        "exclude_genres":  data.get("exclude_genres", []),
        "exclude_tags":    data.get("exclude_tags", []),
        "max_price":       data.get("max_price"),
        "is_free":         data.get("is_free"),
        "reference_games": data.get("reference_games", []),
    }


# ══════════════════════════════════════════════════════════════════════════════
# NODE 2: profile_node
# Завантажує профіль користувача через API. Без LLM.
# ══════════════════════════════════════════════════════════════════════════════

def profile_node(state: AgentState) -> dict:
    """
    Крок 2: завантаження профілю з API.
    GET /api/bot/users/{userId}/profile
    Повертає favoriteGenres, favoriteTags, ownedGameIds, userEmbedding.
    """
    user_id = state["user_id"]
    log_nodes.info("profile_node → userId=%s", user_id)

    profile = _get(f"/users/{user_id}/profile") or {
        "favoriteGenres":  [],
        "favoriteTags":    [],
        "topFeatures":     [],
        "ownedGameIds":    [],
        "wishlistGameIds": [],
        "userEmbedding":   [],
    }

    log_nodes.info(
        "profile_node ← %d owned, %d genres, %d tags, emb_dim=%d",
        len(profile.get("ownedGameIds", [])),
        len(profile.get("favoriteGenres", [])),
        len(profile.get("favoriteTags", [])),
        len(profile.get("userEmbedding", [])),
    )

    return {"user_profile": profile}


# ══════════════════════════════════════════════════════════════════════════════
# NODE 3: planning_node
# LLM будує параметри пошуку на основі intent + профілю.
# НЕ повертає ігри. НЕ ранжує. Тільки search parameters.
# ══════════════════════════════════════════════════════════════════════════════

_PLANNING_SYSTEM = """Ти — планувальник параметрів пошуку ігор.

Твоє завдання: визначити ПАРАМЕТРИ ПОШУКУ на основі:
1. Того, що хоче користувач (intent)
2. Профілю вподобань користувача (його бібліотеки)

ПРАВИЛА:
- required_genres: жанри що ОБОВ'ЯЗКОВО мають бути (з явного запиту)
- boost_genres: жанри для пом'якшеного пошуку (з профілю + суміжні)
- boost_tags: теги що підсилюють релевантність (з профілю користувача)
- exclude_*: небажані характеристики
- search_reasoning: 1-2 речення чому такі параметри

ЗАБОРОНЕНО:
- Повертати назви або ID конкретних ігор
- Ранжувати або оцінювати ігри
- Вирішувати які ігри "кращі"

Повертай ТІЛЬКИ валідний JSON, без markdown, без пояснень."""

_PLANNING_USER_TEMPLATE = """Запит користувача: "{query}"

Intent: {intent}
Явно вказані жанри: {required_genres}
Явно вказані теги: {tags}
Виключити жанри: {exclude_genres}
Виключити теги: {exclude_tags}

Профіль користувача:
- Улюблені жанри (з бібліотеки): {fav_genres}
- Улюблені теги (з бібліотеки): {fav_tags}
- Топ-особливості: {top_features}

Поверни JSON:
{{
  "required_genres": [],
  "boost_genres": [],
  "required_tags": [],
  "boost_tags": [],
  "exclude_genres": [],
  "exclude_tags": [],
  "search_reasoning": "..."
}}"""


def planning_node(state: AgentState) -> dict:
    """
    Крок 3: LLM синтезує параметри пошуку з intent + профілю.
    Не повертає ігри. Не ранжує. Тільки search plan.
    """
    profile = state.get("user_profile") or {}
    log_nodes.info("planning_node → building search plan")

    prompt = _PLANNING_USER_TEMPLATE.format(
        query          = state.get("original_query", ""),
        intent         = state.get("intent", "recommendation"),
        required_genres= state.get("required_genres", []),
        tags           = state.get("tags", []),
        exclude_genres = state.get("exclude_genres", []),
        exclude_tags   = state.get("exclude_tags", []),
        fav_genres     = profile.get("favoriteGenres", [])[:8],
        fav_tags       = profile.get("favoriteTags", [])[:12],
        top_features   = profile.get("topFeatures", [])[:8],
    )

    raw  = llm.invoke([
        SystemMessage(content=_PLANNING_SYSTEM),
        HumanMessage(content=prompt),
    ]).content

    plan = _parse_json_response(raw, {
        "required_genres": state.get("required_genres", []),
        "boost_genres":    state.get("genres", []),
        "required_tags":   state.get("required_tags", []),
        "boost_tags":      state.get("tags", []),
        "exclude_genres":  state.get("exclude_genres", []),
        "exclude_tags":    state.get("exclude_tags", []),
        "search_reasoning": "",
    })

    if not isinstance(plan, dict):
        plan = {
            "required_genres": state.get("required_genres", []),
            "boost_genres": state.get("genres", []),
            "required_tags": state.get("required_tags", []),
            "boost_tags": state.get("tags", []),
            "exclude_genres": state.get("exclude_genres", []),
            "exclude_tags": state.get("exclude_tags", []),
            "search_reasoning": "",
        }

    log_nodes.info(
        "planning_node ← reqGenres=%s boostTags=%s reasoning=%r",
        plan.get("required_genres"),
        plan.get("boost_tags", [])[:5],
        plan.get("search_reasoning", "")[:80],
    )

    return {
        "search_plan":              plan,
        "recommendation_reasoning": plan.get("search_reasoning", ""),
    }


# ══════════════════════════════════════════════════════════════════════════════
# NODE 4: recommendation_node
# ВИКЛЮЧНО API-виклик. Без LLM. Детермінований результат.
# ══════════════════════════════════════════════════════════════════════════════

def recommendation_node(state: AgentState) -> dict:
    """
    Крок 4: виклик /recommend/hybrid через API.
    LLM НЕ ЗАДІЯНИЙ.
    RecommendationEngine виконує:
      Stage 1: pgvector candidate retrieval
      Stage 2: hard filters
      Stage 3: scoring (0.35*emb + 0.30*tag + 0.20*genre + 0.10*rating + 0.05*pop)
               + feedback_bonus
    """
    plan    = state.get("search_plan") or {}
    profile = state.get("user_profile") or {}

    user_id       = state["user_id"]
    owned_ids     = profile.get("ownedGameIds", [])
    user_embedding = profile.get("userEmbedding", [])

    # Об'єднуємо exclude з intent + plan
    exclude_genres = list(set(
        state.get("exclude_genres", []) +
        plan.get("exclude_genres", [])
    ))
    exclude_tags = list(set(
        state.get("exclude_tags", []) +
        plan.get("exclude_tags", [])
    ))

    req = {
        "userId":         user_id,
        "requiredGenres": plan.get("required_genres", state.get("required_genres", [])),
        "boostGenres":    plan.get("boost_genres", state.get("genres", [])),
        "requiredTags":   plan.get("required_tags", state.get("required_tags", [])),
        "boostTags":      plan.get("boost_tags", state.get("tags", [])),
        "excludeGenres":  exclude_genres,
        "excludeTags":    exclude_tags,
        "maxPrice":       state.get("max_price"),
        "isFree":         state.get("is_free"),
        "userEmbedding":  user_embedding,  # float[] — user embedding з UserEmbeddingService
        "ownedGameIds":   owned_ids,
        "limit":          5,
    }

    log_nodes.info(
        "recommendation_node → POST /recommend/hybrid  "
        "reqGenres=%s boostTags=%s embDim=%d ownedCount=%d",
        req["requiredGenres"],
        req["boostTags"][:4],
        len(user_embedding),
        len(owned_ids),
    )

    results = _post("/recommend/hybrid", req)
    count   = len(results) if isinstance(results, list) else 0

    log_nodes.info("recommendation_node ← %d результатів", count)

    return {"recommendation_results": results or []}


# ══════════════════════════════════════════════════════════════════════════════
# NODE 5: explanation_node
# LLM пояснює ЧОМУ ці ігри підійшли. НЕ ранжує. НЕ обирає кращі.
# ══════════════════════════════════════════════════════════════════════════════

_EXPLANATION_SYSTEM = """Ти — пояснювач рекомендацій ігор.
Ігри вже відібрані та відранжовані детермінованим алгоритмом.
Твоє завдання: пояснити ЧОМУ кожна гра підходить цьому користувачу.

Правила:
- 1-2 речення на гру (конкретно, без загальних слів)
- Посилайся на профіль: якщо він любить Sci-Fi і гра має Sci-Fi — вкажи це
- НЕ змінюй порядок ігор
- НЕ кажи "ця гра краща за іншу"
- Відповідай УКРАЇНСЬКОЮ

Повертай ТІЛЬКИ валідний JSON-масив, без markdown:
[{"gameId": 1, "explanation": "..."}, ...]"""


def explanation_node(state: AgentState) -> dict:
    """
    Крок 5: LLM генерує пояснення для кожної рекомендованої гри.
    Порядок ігор НЕ змінюється — він визначений scoring алгоритмом.
    """
    results  = state.get("recommendation_results", [])
    profile  = state.get("user_profile") or {}
    reasoning = state.get("recommendation_reasoning", "")

    if not results:
        log_nodes.info("explanation_node: порожні результати — пропуск")
        return {}

    log_nodes.info("explanation_node → пояснення для %d ігор", len(results))

    prompt = (
        f"Запит: \"{state.get('original_query', '')}\"\n\n"
        f"Профіль користувача:\n"
        f"  Улюблені жанри: {profile.get('favoriteGenres', [])[:6]}\n"
        f"  Улюблені теги: {profile.get('favoriteTags', [])[:10]}\n\n"
        f"Логіка пошуку: {reasoning}\n\n"
        f"Відібрані ігри:\n"
        + "\n".join(
            f"  GameId={g.get('gameId')} | {g.get('name')} | "
            f"Жанри: {g.get('genres', [])} | Теги: {g.get('tags', [])[:6]} | "
            f"Score: {g.get('finalScore', 0):.4f}"
            for g in results
        )
        + "\n\nПоверни JSON-масив з поясненнями."
    )

    raw = llm.invoke([
        SystemMessage(content=_EXPLANATION_SYSTEM),
        HumanMessage(content=prompt),
    ]).content

    explanations_raw = _parse_json_response(raw, [])
    explanations = (
        {e["gameId"]: e.get("explanation", "")
         for e in explanations_raw
         if isinstance(e, dict) and "gameId" in e}
        if isinstance(explanations_raw, list)
        else {}
    )

    # Вставляємо пояснення в результати (порядок не змінюється)
    enriched = [
        {**g, "explanation": explanations.get(g.get("gameId"), "")}
        for g in results
    ]

    log_nodes.info("explanation_node ← %d пояснень додано", len(explanations))
    return {"recommendation_results": enriched}


# ══════════════════════════════════════════════════════════════════════════════
# NODE 6: response_node
# Форматує фінальну відповідь і додає до messages.
# ══════════════════════════════════════════════════════════════════════════════

def response_node(state: AgentState) -> dict:
    """
    Крок 6: форматування фінальної відповіді.
    Скор відображається для прозорості алгоритму.
    """
    results = state.get("recommendation_results", [])

    if not results:
        content = (
            "На жаль, не вдалося знайти ігри за вашими критеріями. "
            "Спробуй послабити обмеження — наприклад, прибрати фільтр по жанру або ціні."
        )
        return {"messages": [AIMessage(content=content)]}

    lines = [f"🎮 Рекомендації за вашим запитом:\n"]

    for i, g in enumerate(results, 1):
        name    = g.get("name", "Невідома гра")
        genres  = ", ".join(g.get("genres", []))
        tags    = ", ".join(g.get("tags", [])[:6])
        rating  = g.get("rating")
        price   = g.get("bestPrice")
        curr    = g.get("currency", "USD")
        is_free = g.get("isFree", False)
        shop    = g.get("shopName", "")
        score   = g.get("finalScore", 0)
        expl    = g.get("explanation", "")

        # Ціна
        if is_free or price == 0:
            price_str = "🆓 Безкоштовно"
        elif price is not None:
            price_str = f"💰 {price:.2f} {curr}"
            if shop:
                price_str += f" ({shop})"
        else:
            price_str = "💰 Ціна невідома"

        # Рейтинг
        rating_str = f"⭐ {rating:.1f}/10" if rating else ""

        lines.append(
            f"**{i}. {name}**  (score: {score:.3f})\n"
            f"   Жанри: {genres}\n"
            f"   Теги: {tags}\n"
            f"   {price_str}"
            + (f"  {rating_str}" if rating_str else "")
            + (f"\n   💬 {expl}" if expl else "")
            + "\n"
        )

    content = "\n".join(lines)
    log_nodes.info("response_node → відповідь готова (%d символів)", len(content))
    return {"messages": [AIMessage(content=content)]}


# ══════════════════════════════════════════════════════════════════════════════
# NODE 7: tools_agent_node
# Збережений agentic підхід для ціни, алертів, загальних питань.
# LLM дозволено тут — але НЕ для рекомендацій.
# ══════════════════════════════════════════════════════════════════════════════

_TOOLS_AGENT_SYSTEM = SystemMessage(content=f"""Ти — асистент GameDB для питань про ціни та алерти.
Відповідай виключно українською.

Ти можеш:
- Перевіряти поточні ціни та знижки на ігри
- Переглядати historical low ціну
- Встановлювати цінові алерти (ТІЛЬКИ після явного підтвердження)
- Розпізнавати скорочені назви ігор

ЗАБОРОНЕНО:
- Рекомендувати ігри (для цього є окремий пайплайн)
- Згадувати назви інструментів у відповіді

Жанри: {_GENRES_STR[:200]}""")


def tools_agent_node(state: AgentState) -> dict:
    """
    Обробляє нерекомендаційні запити (ціна, алерти, бібліотека, загальні питання).
    Використовує utility tools: get_current_offers, get_price_dynamics, set_price_alert.
    """
    log_nodes.debug("tools_agent_node → %d повідомлень", len(state["messages"]))
    response = llm_with_utility_tools.invoke(
        [_TOOLS_AGENT_SYSTEM] + state["messages"]
    )
    if response.tool_calls:
        log_nodes.info("tools_agent_node: викликає %s",
                      [c["name"] for c in response.tool_calls])
    else:
        log_nodes.info("tools_agent_node: фінальна відповідь")
    return {"messages": [response]}


# ══════════════════════════════════════════════════════════════════════════════
# МАРШРУТИЗАЦІЯ
# ══════════════════════════════════════════════════════════════════════════════

def route_after_intent(state: AgentState) -> Literal["profile_node", "tools_agent_node"]:
    """Після intent_node: рекомендація → повний пайплайн, інше → tools_agent."""
    intent = state.get("intent", "general")
    log_graph.info("route_after_intent → intent=%r", intent)
    return "profile_node" if intent == "recommendation" else "tools_agent_node"


def tools_condition(state: AgentState) -> Literal["utility_safe", "utility_sensitive", "__end__"]:
    """Маршрутизація після tools_agent_node."""
    last = state["messages"][-1]
    if not isinstance(last, AIMessage) or not last.tool_calls:
        return "__end__"
    sensitive_names = {t.name for t in _utility_sensitive_tools}
    if any(c["name"] in sensitive_names for c in last.tool_calls):
        log_graph.info("tools_condition → utility_sensitive")
        return "utility_sensitive"
    log_graph.debug("tools_condition → utility_safe")
    return "utility_safe"


# ══════════════════════════════════════════════════════════════════════════════
# ПОБУДОВА ГРАФА
# ══════════════════════════════════════════════════════════════════════════════

builder = StateGraph(AgentState)

# ── Вузли ──────────────────────────────────────────────────────────────────
builder.add_node("intent_node",         intent_node)
builder.add_node("profile_node",        profile_node)
builder.add_node("planning_node",       planning_node)
builder.add_node("recommendation_node", recommendation_node)
builder.add_node("explanation_node",    explanation_node)
builder.add_node("response_node",       response_node)
builder.add_node("tools_agent_node",    tools_agent_node)
builder.add_node("utility_safe",        utility_safe_node)
builder.add_node("utility_sensitive",   utility_sensitive_node)

# ── Ребра: recommendation pipeline ───────────────────────────────────────
builder.add_edge(START,                "intent_node")
builder.add_conditional_edges("intent_node",    route_after_intent)
builder.add_edge("profile_node",       "planning_node")
builder.add_edge("planning_node",      "recommendation_node")
builder.add_edge("recommendation_node","explanation_node")
builder.add_edge("explanation_node",   "response_node")
builder.add_edge("response_node",      END)

# ── Ребра: tools pipeline ─────────────────────────────────────────────────
builder.add_conditional_edges("tools_agent_node", tools_condition)
builder.add_edge("utility_safe",      "tools_agent_node")
builder.add_edge("utility_sensitive", "tools_agent_node")

memory = MemorySaver()
graph  = builder.compile(
    checkpointer=memory,
    interrupt_before=["utility_sensitive"],  # HITL для set_price_alert
)

log_graph.info("Граф скомпільовано ✓  (модель: %s)", MODEL_NAME)


# ══════════════════════════════════════════════════════════════════════════════
# ОСНОВНИЙ ЦИКЛ + HITL
# ══════════════════════════════════════════════════════════════════════════════

def _stream_until_answer(cfg: dict) -> None:
    """Продовжує граф і виводить фінальну відповідь бота."""
    for event in graph.stream(None, cfg, stream_mode="values"):
        last = event["messages"][-1]
        if isinstance(last, AIMessage) and not last.tool_calls:
            print(f"\n🤖 Бот: {last.content}\n")


def main() -> None:
    user_id_str = input("👤 Введіть ваш userId (наприклад 1): ").strip()
    try:
        user_id = int(user_id_str)
    except ValueError:
        print("❌ userId має бути числом.")
        return

    config: dict = {
        "configurable": {
            "thread_id": f"gamedb_v4_{user_id}",
            "user_id":   user_id,
        }
    }

    print(f"\n🤖 GameDB Agent v4 (модель: {MODEL_NAME})")
    print("   Гібридний recommendation engine — без LLM-ранжування")
    print("   Напишіть 'exit' для виходу.\n")

    while True:
        user_input = input("👤 Ти: ").strip()
        if not user_input:
            continue
        if user_input.lower() in ("exit", "quit"):
            break

        log_graph.info("=== НОВИЙ ЗАПИТ: %r ===", user_input[:80])

        # Початковий стан для кожного запиту
        state_input = {
            "messages":                [HumanMessage(content=user_input)],
            "user_id":                 user_id,
            "original_query":          user_input,
            "intent":                  "",
            "genres":                  [],
            "tags":                    [],
            "exclude_genres":          [],
            "exclude_tags":            [],
            "required_genres":         [],
            "required_tags":           [],
            "max_price":               None,
            "is_free":                 None,
            "reference_games":         [],
            "user_profile":            None,
            "search_plan":             None,
            "recommendation_results":  [],
            "recommendation_reasoning": "",
        }

        # Перший прогін
        for event in graph.stream(state_input, config, stream_mode="values"):
            last = event["messages"][-1]
            if isinstance(last, AIMessage) and not last.tool_calls:
                print(f"\n🤖 Бот: {last.content}\n")

        # ── HITL: перевірка зупинки перед utility_sensitive ───────────────
        current = graph.get_state(config)
        if not (current.next and current.next[0] == "utility_sensitive"):
            continue

        pending_calls = current.values["messages"][-1].tool_calls

        for call in pending_calls:
            if call["name"] != "set_price_alert":
                continue

            args         = call["args"]
            game_id      = args.get("game_id")
            target_price = args.get("target_price")

            log_hitl.warning(
                "HITL: set_price_alert  gameId=%s  price=%s  user=%s",
                game_id, target_price, user_id,
            )
            print(
                f"\n⚠️  [HITL] Бот хоче встановити алерт:\n"
                f"   GameId={game_id}, TargetPrice={target_price}"
            )

            decision = input("   Дозволити? (y/n): ").strip().lower()

            if decision == "y":
                log_hitl.info("HITL: ДОЗВОЛЕНО  gameId=%s", game_id)
                print("✅ Дозвіл отримано, виконую…\n")
                _stream_until_answer(config)
            else:
                log_hitl.info("HITL: СКАСОВАНО  gameId=%s", game_id)
                print("❌ Скасовано.\n")
                graph.update_state(config, {"messages": [
                    ToolMessage(
                        tool_call_id=call["id"],
                        name=call["name"],
                        content="ALERT_DENIED_BY_USER",
                    )
                ]})
                _stream_until_answer(config)


if __name__ == "__main__":
    main()
