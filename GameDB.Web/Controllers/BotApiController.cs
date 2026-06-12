using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Domain.Entities;
using GameDB.Infrastructure.AI;
using GameDB.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace GameDB.Web.Controllers;

/// <summary>
/// REST-ендпоінти виключно для Python LangGraph bot-агента v4.
/// Авторизація: заголовок X-Bot-Api-Key (значення з конфігурації BotApi:ApiKey).
///
/// НОВІ ендпоінти:
///   GET  /api/bot/users/{userId}/profile   — профіль вподобань + user embedding
///   POST /api/bot/recommend/hybrid         — гібридний движок рекомендацій
///   POST /api/bot/feedback                 — зворотній зв'язок (like/dislike)
///
/// Збережені ендпоінти:
///   GET  /api/bot/library/{userId}         — бібліотека + вішліст
///   GET  /api/bot/catalog                  — пошук у каталозі
///   POST /api/bot/catalog/semantic         — семантичний пошук (збережений)
///   GET  /api/bot/metadata                 — жанри та теги
///   GET  /api/bot/game/{gameId}/price-context
///   GET  /api/bot/game/{gameId}/details
///   POST /api/bot/game/{gameId}/embedding  — збереження embedding
///   POST /api/bot/alerts                   — ціновий алерт
/// </summary>
[Route("api/bot")]
[ApiController]
public sealed class BotApiController(
    IUserCollectionRepository collections,
    ICatalogRepository        catalog,
    IGameAlertRepository      alertRepo,
    IGameAlertService         alertService,
    IUserProfileService       profileService,
    IRecommendationEngine     recommendationEngine,
    AppDbContext              db,
    IConfiguration            config) : ControllerBase
{
    // ── Auth ──────────────────────────────────────────────────────────────────

    private bool IsAuthorized() =>
        Request.Headers.TryGetValue("X-Bot-Api-Key", out var key)
        && key == config["BotApi:ApiKey"];

    // ════════════════════════════════════════════════════════════════════════
    // НОВІ ЕНДПОІНТИ V4
    // ════════════════════════════════════════════════════════════════════════

    // ── 1. User Preference Profile ────────────────────────────────────────────

    /// <summary>
    /// Повертає профіль вподобань користувача:
    /// - favoriteGenres / favoriteTags — на основі частоти в бібліотеці
    /// - topFeatures — теги без базових жанрів
    /// - ownedGameIds / wishlistGameIds — для виключення та контексту
    /// - userEmbedding — усереднений L2-нормований вектор бібліотеки
    ///
    /// Python: profile_node викликає цей ендпоінт одноразово на початку пайплайну.
    /// </summary>
    [HttpGet("users/{userId:int}/profile")]
    public async Task<IActionResult> GetUserProfile(int userId, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var profile = await profileService.GetProfileAsync(userId, ct);
        return Ok(profile);
    }

    // ── 2. Hybrid Recommendation Engine ──────────────────────────────────────

    /// <summary>
    /// Гібридний движок рекомендацій. LLM НЕ задіяний.
    ///
    /// Stage 1: pgvector cosine distance (top 300 candidates)
    /// Stage 2: hard filters (owned / excluded / price / free / required)
    /// Stage 3: детермінований scoring за формулою
    ///          0.35*embSim + 0.30*tagSim + 0.20*genreSim + 0.10*rating + 0.05*pop
    ///          + feedback_bonus (≤ 0.15)
    ///
    /// Python: recommendation_node викликає цей ендпоінт, не LLM.
    /// </summary>
    [HttpPost("recommend/hybrid")]
    public async Task<IActionResult> RecommendHybrid(
        [FromBody] HybridRecommendRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        if (req.UserId <= 0)
            return BadRequest(new { error = "UserId має бути > 0." });

        var results = await recommendationEngine.RecommendHybridAsync(req, ct);
        return Ok(results);
    }

    // ── 3. User Feedback (Like / Dislike) ─────────────────────────────────────

    /// <summary>
    /// Зберігає або оновлює відгук користувача на гру.
    /// Feedback впливає на feedback_bonus у наступних рекомендаціях.
    ///
    /// POST /api/bot/feedback
    /// { "userId": 1, "gameId": 123, "isLiked": true }
    /// </summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> SubmitFeedback(
        [FromBody] FeedbackRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var existing = await db.Set<UserGameFeedback>()
            .FirstOrDefaultAsync(f => f.UserId == req.UserId && f.GameId == req.GameId, ct);

        if (existing is null)
        {
            db.Set<UserGameFeedback>().Add(new UserGameFeedback
            {
                UserId    = req.UserId,
                GameId    = req.GameId,
                IsLiked   = req.IsLiked,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.IsLiked   = req.IsLiked;
            existing.CreatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            message = $"Feedback для gameId={req.GameId}: {(req.IsLiked ? "👍 liked" : "👎 disliked")}",
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    // ЗБЕРЕЖЕНІ ЕНДПОІНТИ (без змін)
    // ════════════════════════════════════════════════════════════════════════

    // ── 4. Library + Wishlist ─────────────────────────────────────────────────

    [HttpGet("library/{userId:int}")]
    public async Task<IActionResult> GetLibrary(int userId, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var wishlist = await collections.GetWishlistAsync(userId, ct);
        var library  = await collections.GetLibraryAsync(userId, ct);
        var ownedIds = library.Select(g => g.GameId).Distinct().ToList();

        var ownedGames = await db.Set<Game>()
            .AsNoTracking()
            .Where(g => ownedIds.Contains(g.GameId))
            .Select(g => new
            {
                g.GameId,
                g.Name,
                Genres = g.Genres.Select(gg => gg.Name).ToList(),
                Tags   = g.Tags.Select(gt => gt.Name).ToList(),
            })
            .ToListAsync(ct);

        return Ok(new
        {
            OwnedGames      = ownedGames,
            OwnedGameIds    = ownedIds,
            WishlistGames   = wishlist.Select(g => new
            {
                g.GameId,
                g.Name,
                g.BestFinalPrice,
                g.BestDiscount,
                g.BestCurrency,
                g.Rating,
                g.AddedAt,
            }),
            WishlistGameIds = wishlist.Select(g => g.GameId).Distinct().ToList(),
        });
    }

    // ── 5. Catalog Search ─────────────────────────────────────────────────────

    [HttpGet("catalog")]
    public async Task<IActionResult> SearchCatalog(
        [FromQuery] string?  search,
        [FromQuery] string?  genres,
        [FromQuery] string?  tags,
        [FromQuery] decimal? maxPrice,
        [FromQuery] decimal? minPrice,
        [FromQuery] int?     minDiscount,
        [FromQuery] double?  minRating,
        [FromQuery] bool?    isFree,
        [FromQuery] int      page     = 1,
        [FromQuery] int      pageSize = 10,
        CancellationToken    ct       = default)
    {
        if (!IsAuthorized()) return Unauthorized();

        var genreIds = new List<int>();
        if (!string.IsNullOrWhiteSpace(genres))
        {
            var names = genres.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            genreIds = await db.Set<Genre>()
                .Where(g => names.Any(n => EF.Functions.ILike(g.Name, $"%{n}%")))
                .Select(g => g.GenreId)
                .ToListAsync(ct);
        }

        var tagIds = new List<int>();
        if (!string.IsNullOrWhiteSpace(tags))
        {
            var names = tags.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            tagIds = await db.Set<Tag>()
                .Where(t => names.Any(n => EF.Functions.ILike(t.Name, $"%{n}%")))
                .Select(t => t.TagId)
                .ToListAsync(ct);
        }

        var filter = new CatalogFilterDto
        {
            Search      = search,
            GenreIds    = genreIds,
            TagIds      = tagIds,
            MinPrice    = minPrice,
            MaxPrice    = maxPrice,
            MinDiscount = minDiscount,
            MinRating   = minRating,
            IsFree      = isFree,
            Page        = Math.Max(1, page),
            PageSize    = Math.Clamp(pageSize, 1, 20),
            SortBy      = CatalogSortBy.Popularity,
            SortDesc    = true,
        };

        var items = await catalog.GetPagedAsync(filter, ct);

        return Ok(new
        {
            items = items.Select(g => new
            {
                g.GameId,
                g.Name,
                g.Genres,
                g.Rating,
                g.BestDiscount,
                g.IsFree,
            }),
        });
    }

    // ── 6. Semantic Search (збережений, але BAAI/bge-base-en-v1.5 на Python-стороні) ──

    public record SemanticSearchRequest(
        float[]  QueryEmbedding,
        decimal? MaxPrice       = null,
        bool?    IsFree         = null,
        string?  ExcludeGameIds = null,
        int      Limit          = 10);

    [HttpPost("catalog/semantic")]
    public async Task<IActionResult> SemanticSearch(
        [FromBody] SemanticSearchRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var excludeIds = ParseIds(req.ExcludeGameIds);
        var embedding  = new Pgvector.Vector(req.QueryEmbedding);

        var query = db.Set<Game>()
            .Include(g => g.Tags)
            .Include(g => g.Genres)
            .AsNoTracking()
            .Where(g => g.Embedding != null);

        if (excludeIds.Count > 0)
            query = query.Where(g => !excludeIds.Contains(g.GameId));

        if (req.MaxPrice.HasValue)
            query = query.Where(g => g.GameExternalIds
                .Any(ge => ge.GameOffers.Any(o => o.FinalPrice <= req.MaxPrice)));

        var results = await query
            .OrderBy(g => g.Embedding!.CosineDistance(embedding))
            .Take(req.Limit)
            .Select(g => new
            {
                g.GameId,
                g.Name,
                Genres        = g.Genres.Select(gg => gg.Name).ToList(),
                Tags          = g.Tags.Select(t => t.Name).Take(10).ToList(),
                g.Rating,
                SemanticScore = 1.0 - g.Embedding!.CosineDistance(embedding),
            })
            .ToListAsync(ct);

        return Ok(results);
    }

    // ── 7. Metadata ───────────────────────────────────────────────────────────

    [HttpGet("metadata")]
    public async Task<IActionResult> GetMetadata(CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var genres = await db.Set<Genre>()
            .Select(g => g.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        var tags = await db.Set<Tag>()
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        return Ok(new { Genres = genres, Tags = tags });
    }

    // ── 8. Price Context ──────────────────────────────────────────────────────

    [HttpGet("game/{gameId:int}/price-context")]
    public async Task<IActionResult> GetPriceContext(int gameId, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();
        try
        {
            var ctx = await alertRepo.GetPriceContextAsync(gameId, userId: null, ct);
            return Ok(ctx);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ── 9. Game Details ───────────────────────────────────────────────────────

    [HttpGet("game/{gameId:int}/details")]
    public async Task<IActionResult> GetGameDetails(int gameId, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var game = await db.Set<Game>()
            .Include(g => g.Tags)
            .Include(g => g.Genres)
            .Include(g => g.GameExternalIds)
                .ThenInclude(ge => ge.GameOffers)
            .Include(g => g.GameExternalIds)
                .ThenInclude(ge => ge.Shop)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.GameId == gameId, ct);

        if (game is null)
            return NotFound(new { error = $"Гру GameId={gameId} не знайдено." });

        var offers = game.GameExternalIds
            .SelectMany(ge => ge.GameOffers.Select(o => new
            {
                ShopName   = ge.Shop?.Name ?? "Unknown",
                FinalPrice = o.FinalPrice,
                BasePrice  = o.CurrentPrice,
                Discount   = o.CurrentDiscount,
                o.Currency,
            }))
            .OrderBy(o => o.FinalPrice)
            .ToList();

        return Ok(new
        {
            game.GameId,
            game.Name,
            Genres      = game.Genres.Select(gg => gg.Name).OrderBy(n => n).ToList(),
            Tags        = game.Tags.Select(gt => gt.Name).OrderBy(n => n).ToList(),
            game.Rating,
            Description = game.Description,
            Offers      = offers,
        });
    }

    // ── 10. Save Embedding ────────────────────────────────────────────────────

    public record SaveEmbeddingRequest(List<float> Embedding);

    [HttpPost("game/{gameId:int}/embedding")]
    public async Task<IActionResult> SaveEmbedding(
        int gameId,
        [FromBody] SaveEmbeddingRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        if (req.Embedding is not { Count: > 0 })
            return BadRequest(new { error = "Вектор ембеддінгу не може бути порожнім." });

        var game = await db.Set<Game>()
            .FirstOrDefaultAsync(g => g.GameId == gameId, ct);

        if (game is null)
            return NotFound(new { error = $"Гру GameId={gameId} не знайдено." });

        game.Embedding = new Pgvector.Vector(req.Embedding.ToArray());
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            message = $"Embedding для гри #{gameId} ({game.Name}) збережено.",
        });
    }

    // ── 11. Price Alert ───────────────────────────────────────────────────────

    public record CreateBotAlertRequest(
        int     UserId,
        int     GameId,
        decimal TargetPrice,
        int?    ShopId = null);

    [HttpPost("alerts")]
    public async Task<IActionResult> CreateAlert(
        [FromBody] CreateBotAlertRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var dto = new SavePriceAlertDto
        {
            GameId         = req.GameId,
            TargetPrice    = req.TargetPrice,
            AutoUpdate     = false,
            AutoUpdateMode = AlertAutoUpdateMode.BeatLowest,
            ShopId         = req.ShopId,
        };

        try
        {
            await alertService.SaveAlertAsync(req.UserId, dto, ct);
            return Ok(new
            {
                success = true,
                message = $"Алерт на гру #{req.GameId} за ціною {req.TargetPrice} встановлено.",
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<int> ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }
}