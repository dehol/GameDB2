using GameDB.Application.DTOs;
using GameDB.Application.Services;
using GameDB.Domain.Entities;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace GameDB.Infrastructure.AI;

/// <summary>
/// Гібридний рушій рекомендацій. LLM не задіяний — лише детермінований код.
///
/// Stage 1 — Candidate Retrieval:  pgvector cosine distance (top 300)
/// Stage 2 — Hard Filters:         owned / excluded / price / free
/// Stage 3 — Similarity Scoring:   формула нижче
/// Stage 4 — Feedback Boost:       бонус за liked games
///
/// score = 0.35 * embedding_similarity
///       + 0.30 * tag_similarity
///       + 0.20 * genre_similarity
///       + 0.10 * normalized_rating
///       + 0.05 * popularity
///
/// final_score = score + feedback_bonus   (feedback_bonus ≤ 0.15)
/// </summary>
public sealed class RecommendationEngine(
    AppDbContext                    db,
    ILogger<RecommendationEngine>   logger) : IRecommendationEngine
{
    // ── Вагові коефіцієнти ────────────────────────────────────────────────────
    private const double W_EMBEDDING = 0.35;
    private const double W_TAG       = 0.30;
    private const double W_GENRE     = 0.20;
    private const double W_RATING    = 0.10;
    private const double W_POPULAR   = 0.05;

    // ── Feedback ──────────────────────────────────────────────────────────────
    private const double FEEDBACK_TAG_BONUS   = 0.02; // за кожен спільний тег
    private const double FEEDBACK_GENRE_BONUS = 0.03; // за кожен спільний жанр
    private const double FEEDBACK_CAP         = 0.15; // максимальний feedback_bonus

    // ── Candidate limit ───────────────────────────────────────────────────────
    private const int CANDIDATE_LIMIT = 3000;

    // ── Default embedding similarity коли embedding відсутній ────────────────
    private const double DEFAULT_EMB_SIM = 0.5;

    public async Task<List<RecommendedGameDto>> RecommendHybridAsync(
        HybridRecommendRequest req, CancellationToken ct)
    {
        req.Limit = Math.Clamp(req.Limit, 1, 20);

        logger.LogInformation(
            "RecommendHybrid: userId={UserId} reqGenres=[{RG}] boostTags=[{BT}] limit={Lim}",
            req.UserId,
            string.Join(", ", req.RequiredGenres),
            string.Join(", ", req.BoostTags),
            req.Limit);

        // ════════════════════════════════════════════════════════════════════
        // STAGE 1 — Candidate Retrieval via pgvector
        // ════════════════════════════════════════════════════════════════════
        var candidates = await RetrieveCandidatesAsync(req, ct);

        logger.LogDebug("Stage 1 candidates: {Count}", candidates.Count);

        // ════════════════════════════════════════════════════════════════════
        // STAGE 2 — Hard Filters (in-memory)
        // ════════════════════════════════════════════════════════════════════
        var ownedSet        = req.OwnedGameIds.ToHashSet();
        var excludeGenreSet = ToLowerSet(req.ExcludeGenres);
        var excludeTagSet   = ToLowerSet(req.ExcludeTags);
        var reqGenreSet     = ToLowerSet(req.RequiredGenres);
        var reqTagSet       = ToLowerSet(req.RequiredTags);

        var filtered = candidates.Where(g => PassesHardFilters(
            g, ownedSet, excludeGenreSet, excludeTagSet,
            reqGenreSet, reqTagSet, req.MaxPrice, req.IsFree)).ToList();

        logger.LogDebug("Stage 2 after filters: {Count}", filtered.Count);

        if (filtered.Count == 0)
        {
            logger.LogWarning("RecommendHybrid: порожній результат після hard filters");
            return [];
        }

        // ════════════════════════════════════════════════════════════════════
        // STAGE 3 — Similarity Scoring
        // ════════════════════════════════════════════════════════════════════
        var prefTagSet   = ToLowerSet(req.BoostTags.Concat(req.RequiredTags));
        var prefGenreSet = ToLowerSet(req.BoostGenres.Concat(req.RequiredGenres));

        // Нормалізація рейтингу
        double maxRating = filtered.Max(g => g.Rating ?? 0);
        if (maxRating <= 0) maxRating = 10.0;

        // User embedding (null → DEFAULT_EMB_SIM для всіх)
        float[]? userEmb = req.UserEmbedding.Count > 0
            ? req.UserEmbedding.ToArray()
            : null;

        // Feedback sets: теги та жанри вподобаних ігор
        var (likedTagSet, likedGenreSet) = await GetFeedbackSetsAsync(req.UserId, ct);

        var scored = filtered
            .Select(g => ScoreGame(
                g, userEmb, prefTagSet, prefGenreSet,
                likedTagSet, likedGenreSet, maxRating))
            .OrderByDescending(x => x.FinalScore)
            .Take(req.Limit)
            .ToList();

        logger.LogInformation("RecommendHybrid: повертаємо {Count} результатів", scored.Count);

        return scored.Select(MapToDto).ToList();
    }

    // ── Stage 1: Retrieval ────────────────────────────────────────────────────

    private async Task<List<Game>> RetrieveCandidatesAsync(
        HybridRecommendRequest req, CancellationToken ct)
    {
        var baseQuery = db.Set<Game>()
            .Include(g => g.Tags)
            .Include(g => g.Genres)
            .Include(g => g.GameExternalIds).ThenInclude(ge => ge.GameOffers)
            .Include(g => g.GameExternalIds).ThenInclude(ge => ge.Shop)
            .AsNoTracking();
        var test = baseQuery.Count();
        // Векторний пошук якщо є user embedding
        if (req.UserEmbedding.Count > 0)
        {
            var pgVec = new Vector(req.UserEmbedding.ToArray());

            var result =  await baseQuery
                .Where(g => g.Embedding != null)
                .OrderBy(g => g.Embedding!.CosineDistance(pgVec))
                .Take(CANDIDATE_LIMIT)
                .ToListAsync(ct);
            logger.LogInformation("Векторний пошук: знайдено {Count} кандидатів (ліміт {Limit})", result.Count, CANDIDATE_LIMIT);
return result;
        }

        // Fallback: genre/tag-based retrieval (без embedding)
        return await FallbackRetrievalAsync(req, baseQuery, ct);
    }

    private static async Task<List<Game>> FallbackRetrievalAsync(
        HybridRecommendRequest req,
        IQueryable<Game>       baseQuery,
        CancellationToken      ct)
    {
        // Фільтруємо за required/boost жанрами якщо є
        var targetGenres = req.RequiredGenres.Concat(req.BoostGenres)
            .Select(g => g.ToLowerInvariant()).ToList();

        if (targetGenres.Count > 0)
        {
            baseQuery = baseQuery.Where(g =>
                g.Genres.Any(gg =>
                    targetGenres.Any(tg =>
                        EF.Functions.ILike(gg.Name, $"%{tg}%"))));
        }
        
        return await baseQuery
            .OrderByDescending(g => g.Rating)
            .Take(CANDIDATE_LIMIT)
            .ToListAsync(ct);
    }

    // ── Stage 2: Hard Filter predicate ───────────────────────────────────────

    private static bool PassesHardFilters(
        Game              g,
        HashSet<int>      ownedSet,
        HashSet<string>   excludeGenreSet,
        HashSet<string>   excludeTagSet,
        HashSet<string>   reqGenreSet,
        HashSet<string>   reqTagSet,
        decimal?          maxPrice,
        bool?             isFree)
    {
        // Виключаємо власні ігри
        if (ownedSet.Contains(g.GameId)) return false;

        // Заборонені жанри
        if (excludeGenreSet.Count > 0 &&
            g.Genres.Any(gg => excludeGenreSet.Contains(gg.Name.ToLowerInvariant())))
            return false;

        // Заборонені теги
        if (excludeTagSet.Count > 0 &&
            g.Tags.Any(t => excludeTagSet.Contains(t.Name.ToLowerInvariant())))
            return false;

        // Обов'язкові жанри: хоча б один з reqGenreSet має міститись у назві жанру
        if (reqGenreSet.Count > 0 &&
            !reqGenreSet.Any(rg =>
                g.Genres.Any(gg =>
                    gg.Name.Contains(rg, StringComparison.OrdinalIgnoreCase))))
            return false;

        // Обов'язкові теги
        if (reqTagSet.Count > 0 &&
            !reqTagSet.Any(rt =>
                g.Tags.Any(t =>
                    t.Name.Contains(rt, StringComparison.OrdinalIgnoreCase))))
            return false;

        // Ціновий фільтр
        var offers = g.GameExternalIds.SelectMany(ge => ge.GameOffers).ToList();

        if (isFree == true)
        {
            if (!offers.Any(o => o.FinalPrice == 0)) return false;
        }
        else if (isFree == false)
        {
            if (!offers.Any(o => o.FinalPrice > 0)) return false;
        }

        if (maxPrice.HasValue)
        {
            var minPaidPrice = offers
                .Where(o => o.FinalPrice.HasValue && o.FinalPrice > 0)
                .Min(o => (decimal?)o.FinalPrice);

            if (minPaidPrice == null || minPaidPrice > maxPrice.Value) return false;
        }

        return true;
    }

    // ── Stage 3: Scoring ──────────────────────────────────────────────────────

    private static ScoredGame ScoreGame(
        Game            g,
        float[]?        userEmb,
        HashSet<string> prefTagSet,
        HashSet<string> prefGenreSet,
        HashSet<string> likedTagSet,
        HashSet<string> likedGenreSet,
        double          maxRating)
    {
        var gameTags   = g.Tags.Select(t => t.Name.ToLowerInvariant()).ToHashSet();
        var gameGenres = g.Genres.Select(gg => gg.Name.ToLowerInvariant()).ToHashSet();

        // 1. Embedding similarity
        double embSim = DEFAULT_EMB_SIM;
        if (userEmb != null && g.Embedding != null)
        {
            embSim = CosineSimilarity(userEmb, g.Embedding.ToArray());
            embSim = Math.Clamp(embSim, 0.0, 1.0);
        }

        // 2. Tag similarity: |gameTags ∩ prefTags| / max(|gameTags|, |prefTags|)
        double tagSim = 0;
        if (prefTagSet.Count > 0 && gameTags.Count > 0)
        {
            int intersection = gameTags.Intersect(prefTagSet).Count();
            tagSim = (double)intersection / Math.Max(prefTagSet.Count, gameTags.Count);
        }

        // 3. Genre similarity
        double genreSim = 0;
        if (prefGenreSet.Count > 0 && gameGenres.Count > 0)
        {
            int intersection = gameGenres.Intersect(prefGenreSet).Count();
            genreSim = (double)intersection / Math.Max(prefGenreSet.Count, gameGenres.Count);
        }

        // 4. Normalized rating
        double normRating = (g.Rating ?? 0) / maxRating;

        // 5. Popularity (0 поки нема reviewCount — weight 0.05 мінімальний вплив)
        double popularity = 0;

        // Base score
        double baseScore =
            W_EMBEDDING * embSim     +
            W_TAG       * tagSim     +
            W_GENRE     * genreSim   +
            W_RATING    * normRating +
            W_POPULAR   * popularity;

        // Feedback boost
        double feedbackBonus = 0;
        if (likedTagSet.Count > 0 || likedGenreSet.Count > 0)
        {
            int tagOverlap   = gameTags.Intersect(likedTagSet).Count();
            int genreOverlap = gameGenres.Intersect(likedGenreSet).Count();
            feedbackBonus    = FEEDBACK_TAG_BONUS * tagOverlap +
                               FEEDBACK_GENRE_BONUS * genreOverlap;
            feedbackBonus    = Math.Min(feedbackBonus, FEEDBACK_CAP);
        }

        return new ScoredGame(
            Game:          g,
            FinalScore:    baseScore + feedbackBonus,
            EmbSim:        embSim,
            TagSim:        tagSim,
            GenreSim:      genreSim,
            NormRating:    normRating,
            FeedbackBonus: feedbackBonus);
    }

    // ── Feedback sets ─────────────────────────────────────────────────────────

    private async Task<(HashSet<string> Tags, HashSet<string> Genres)> GetFeedbackSetsAsync(
        int userId, CancellationToken ct)
    {
        var likedIds = await db.Set<UserGameFeedback>()
            .Where(f => f.UserId == userId && f.IsLiked)
            .Select(f => f.GameId)
            .ToListAsync(ct);

        if (likedIds.Count == 0)
            return ([], []);

        var likedGames = await db.Set<Game>()
            .Include(g => g.Tags)
            .Include(g => g.Genres)
            .Where(g => likedIds.Contains(g.GameId))
            .AsNoTracking()
            .ToListAsync(ct);

        var tags = likedGames
            .SelectMany(g => g.Tags.Select(t => t.Name.ToLowerInvariant()))
            .ToHashSet();
        var genres = likedGames
            .SelectMany(g => g.Genres.Select(gg => gg.Name.ToLowerInvariant()))
            .ToHashSet();

        return (tags, genres);
    }

    // ── DTO mapping ───────────────────────────────────────────────────────────

    private static RecommendedGameDto MapToDto(ScoredGame x)
    {
        var g = x.Game;

        var bestOffer = g.GameExternalIds
            .SelectMany(ge => ge.GameOffers
                .Select(o => new
                {
                    o.FinalPrice,
                    o.Currency,
                    ShopName = ge.Shop?.Name,
                }))
            .Where(o => o.FinalPrice.HasValue)
            .MinBy(o => o.FinalPrice);

        return new RecommendedGameDto
        {
            GameId    = g.GameId,
            Name      = g.Name,
            Genres    = g.Genres.Select(gg => gg.Name).ToList(),
            Tags      = g.Tags.Select(t => t.Name).Take(12).ToList(),
            Rating    = g.Rating,
            BestPrice = bestOffer?.FinalPrice,
            Currency  = bestOffer?.Currency,
            ShopName  = bestOffer?.ShopName,
            IsFree    = bestOffer?.FinalPrice == 0,
            // Обрізаємо опис до 300 символів — достатньо для LLM explanation_node
            // без зайвого навантаження на контекст агента
            Description = g.Description is { Length: > 0 }
                ? (g.Description.Length > 300
                    ? g.Description[..300] + "…"
                    : g.Description)
                : null,
            FinalScore = Math.Round(x.FinalScore, 4),
            ScoreBreakdown = new ScoreBreakdownDto
            {
                EmbeddingSimilarity = Math.Round(x.EmbSim,        4),
                TagSimilarity       = Math.Round(x.TagSim,        4),
                GenreSimilarity     = Math.Round(x.GenreSim,      4),
                NormalizedRating    = Math.Round(x.NormRating,    4),
                Popularity          = 0,
                FeedbackBonus       = Math.Round(x.FeedbackBonus, 4),
            },
        };
    }

    // ── Math ──────────────────────────────────────────────────────────────────

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        return normA > 0 && normB > 0
            ? dot / (Math.Sqrt(normA) * Math.Sqrt(normB))
            : 0;
    }

    private static HashSet<string> ToLowerSet(IEnumerable<string> source) =>
        source.Select(s => s.ToLowerInvariant()).ToHashSet();

    // ── Internal record ───────────────────────────────────────────────────────

    private sealed record ScoredGame(
        Game   Game,
        double FinalScore,
        double EmbSim,
        double TagSim,
        double GenreSim,
        double NormRating,
        double FeedbackBonus);
}