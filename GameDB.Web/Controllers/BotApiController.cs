using System.Numerics;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector;
namespace GameDB.Web.Controllers;

/// <summary>
/// REST-ендпоінти виключно для Python LangGraph bot-агента.
/// Авторизація: заголовок X-Bot-Api-Key (значення з конфігурації BotApi:ApiKey).
/// </summary>
[Route("api/bot")]
[ApiController]
public sealed class BotApiController(
    IUserCollectionRepository collections,
    ICatalogRepository        catalog,
    IGameAlertRepository      alertRepo,
    IGameAlertService         alertService,
    AppDbContext               db,
    IConfiguration             config) : ControllerBase
{
    // ── Auth ──────────────────────────────────────────────────────────────────

    private bool IsAuthorized() =>
        Request.Headers.TryGetValue("X-Bot-Api-Key", out var key)
        && key == config["BotApi:ApiKey"];

    // ── 1. Бібліотека + Вішліст ───────────────────────────────────────────────

    /// <summary>
    /// Повертає власну бібліотеку користувача та його вішліст із поточними цінами.
    /// Агент використовує ці дані, щоб виключати вже куплені ігри з рекомендацій
    /// або шукати знижки на ігри зі списку бажаного.
    /// </summary>
    [HttpGet("library/{userId:int}")]
    public async Task<IActionResult> GetLibrary(int userId, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        var wishlist = await collections.GetWishlistAsync(userId, ct);
        var library  = await collections.GetLibraryAsync(userId, ct);

        var ownedIds = library.Select(g => g.GameId).Distinct().ToList();

        // Витягуємо ігри бібліотеки з тегами та жанрами для контекстного аналізу
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
            // Повний список ігор бібліотеки (з жанрами/тегами для рекомендацій)
            OwnedGames = ownedGames,

            // Вішліст з поточними цінами — агент бачить, чи є знижка
            WishlistGames = wishlist.Select(g => new
            {
                g.GameId,
                g.Name,
                g.BestFinalPrice,
                g.BestDiscount,
                g.BestCurrency,
                g.Rating,
                g.AddedAt,
            }),

            // Для швидкого O(1) пошуку «чи є гра у вішліст»
            WishlistGameIds = wishlist.Select(g => g.GameId).Distinct().ToList(),
        });
    }

    // ── 2. Пошук у каталозі ───────────────────────────────────────────────────

    /// <summary>
    /// Пошук у каталозі з фільтрами. Агент передає розпарсені параметри запиту користувача.
    /// </summary>
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
            var names = genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            genreIds = await db.Set<Genre>()
                .Where(g => names.Any(n => EF.Functions.ILike(g.Name, $"%{n}%")))
                .Select(g => g.GenreId)
                .ToListAsync(ct);
        }

        var tagIds = new List<int>();
        if (!string.IsNullOrWhiteSpace(tags))
        {
            var names = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

        var (items, total) = await catalog.GetPagedAsync(filter, ct);

        return Ok(new
        {
            total,
            items = items.Select(g => new
            {
                g.GameId,
                g.Name,
                g.Genres,
                g.Rating,
                g.BestFinalPrice,
                g.BestDiscount,
                g.BestCurrency,
                g.BestShopName,
                g.IsFree,
            }),
        });
    }

    // ── 3. Метадані каталогу (жанри та теги) ──────────────────────────────────

    /// <summary>
    /// Повертає всі доступні жанри та теги. Агент викликає цей ендпоінт одноразово
    /// при ініціалізації, щоб знати, які значення передавати у фільтри.
    /// </summary>
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

    // ── 4. Ціновий контекст гри ───────────────────────────────────────────────

    /// <summary>
    /// Поточна ціна, знижка, мінімум за всю історію — все необхідне агенту,
    /// щоб відповісти на «чи варто купувати зараз?».
    /// </summary>
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

    // ── 5. Повні деталі гри для аналізу агентом ──────────────────────────────

    /// <summary>
    /// Детальна інформація про гру: жанри, теги, всі оффери по магазинах.
    /// Використовується для глибокого аналізу конкретної гри.
    /// </summary>
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
                ShopName   = ge.Shop?.Name ?? "Unknown",   // FIX: null-safe
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
            Genres = game.Genres.Select(gg => gg.Name).OrderBy(n => n).ToList(),
            Tags   = game.Tags.Select(gt => gt.Name).OrderBy(n => n).ToList(),
            game.Rating,
            Offers = offers,
        });
    }

    // ── 6. Рекомендації з ваговим скорингом ───────────────────────────────────

    // ── 6. Рекомендації з ваговим скорингом ───────────────────────────────────

    /// <summary>
    /// Повертає схожі ігри на основі вагового скорингу по тегах і жанрах.
    /// </summary>
    [HttpGet("catalog/recommend")]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] string?  referenceGameIds,
        [FromQuery] string?  excludeGameIds,   // ← НОВЕ: список ігор для жорсткого ігнору (наприклад, вже куплені)
        [FromQuery] string?  boostTags,
        [FromQuery] string?  boostGenres,
        [FromQuery] string?  excludeTags,
        [FromQuery] string?  excludeGenres,
        [FromQuery] string?  reqiredGenres,
        [FromQuery] string?  reqiredTags,
        [FromQuery] float?   minRating,
        [FromQuery] decimal? maxPrice,
        [FromQuery] bool?    isFree,
        [FromQuery] int      limit = 10,
        CancellationToken    ct    = default)
    {
        if (!IsAuthorized()) return Unauthorized();

        static HashSet<string> Csv(string? s) =>
            string.IsNullOrWhiteSpace(s) ? []
            : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var boostTagSet     = Csv(boostTags);
        var boostGenreSet   = Csv(boostGenres);
        var excludeTagSet   = Csv(excludeTags);
        var excludeGenreSet = Csv(excludeGenres);
        var reqTagSet       = Csv(reqiredTags);
        var reqGenreSet     = Csv(reqiredGenres);

        // ── Крок 1: базові теги/жанри з референсних ігор ──────────────────────

        var baseTagNames   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseGenreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var refGameIds = new List<int>();
        if (!string.IsNullOrWhiteSpace(referenceGameIds))
        {
            refGameIds = referenceGameIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }

        if (refGameIds.Count > 0)
        {
            var refGames = await db.Set<Game>()
                .Include(g => g.Tags)
                .Include(g => g.Genres)
                .AsNoTracking()
                .Where(g => refGameIds.Contains(g.GameId))
                .ToListAsync(ct);

            foreach (var refGame in refGames)
            {
                foreach (var tag   in refGame.Tags)   baseTagNames.Add(tag.Name);
                foreach (var genre in refGame.Genres)  baseGenreNames.Add(genre.Name);
            }
        }

        // ── Крок 1.Б: Парсинг ігор для виключення (Куплені/Owned) ──────────────

        var exGameIds = new List<int>(); // ← НОВЕ
        if (!string.IsNullOrWhiteSpace(excludeGameIds))
        {
            exGameIds = excludeGameIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }

        var allTargetTagNames = baseTagNames
            .Union(boostTagSet, StringComparer.OrdinalIgnoreCase)
            .Except(excludeTagSet, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allTargetGenreNames = baseGenreNames
            .Union(boostGenreSet, StringComparer.OrdinalIgnoreCase)
            .Except(excludeGenreSet, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ── Крок 2: теги/жанри → словники {Id → isBoosted} для скорингу ──────

        var tagMap   = new Dictionary<int, bool>();
        var genreMap = new Dictionary<int, bool>();

        if (allTargetTagNames.Count > 0)
        {
            var dbTags = await db.Set<Tag>().Select(t => new { t.TagId, t.Name }).ToListAsync(ct);
            foreach (var r in dbTags.Where(t => allTargetTagNames.Any(n => t.Name.Contains(n, StringComparison.OrdinalIgnoreCase))))
            {
                tagMap[r.TagId] = boostTagSet.Any(b => r.Name.Contains(b, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (allTargetGenreNames.Count > 0)
        {
            var dbGenres = await db.Set<Genre>().Select(g => new { g.GenreId, g.Name }).ToListAsync(ct);
            foreach (var r in dbGenres.Where(g => allTargetGenreNames.Any(n => g.Name.Contains(n, StringComparison.OrdinalIgnoreCase))))
            {
                genreMap[r.GenreId] = boostGenreSet.Any(b => r.Name.Contains(b, StringComparison.OrdinalIgnoreCase));
            }
        }

        // ── Крок 3: ID для жорсткого виключення по тегах/жанрах ──────────────

        var excludeTagIds = new List<int>();
        if (excludeTagSet.Count > 0)
        {
            var dbTags = await db.Set<Tag>().Select(t => new { t.TagId, t.Name }).ToListAsync(ct);
            excludeTagIds = dbTags.Where(t => excludeTagSet.Any(n => t.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).Select(t => t.TagId).ToList();
        }

        var excludeGenreIds = new List<int>();
        if (excludeGenreSet.Count > 0)
        {
            var dbGenres = await db.Set<Genre>().Select(g => new { g.GenreId, g.Name }).ToListAsync(ct);
            excludeGenreIds = dbGenres.Where(g => excludeGenreSet.Any(n => g.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).Select(g => g.GenreId).ToList();
        }

        // ── Крок 4: ID для обов'язкових тегів/жанрів (required) ──────────────

        var requiredTagIds = new List<int>();
        if (reqTagSet.Count > 0)
        {
            var dbTags = await db.Set<Tag>().Select(t => new { t.TagId, t.Name }).ToListAsync(ct);
            requiredTagIds = dbTags.Where(t => reqTagSet.Any(n => t.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).Select(t => t.TagId).ToList();
        }

        var requiredGenreIds = new List<int>();
        if (reqGenreSet.Count > 0)
        {
            var dbGenres = await db.Set<Genre>().Select(g => new { g.GenreId, g.Name }).ToListAsync(ct);
            requiredGenreIds = dbGenres.Where(g => reqGenreSet.Any(n => g.Name.Contains(n, StringComparison.OrdinalIgnoreCase))).Select(g => g.GenreId).ToList();
        }

        limit = Math.Clamp(limit, 1, 20);

        // ── Крок 5: формуємо базовий запит ───────────────────────────────────

        IQueryable<Game> q = db.Set<Game>()
            .Include(g => g.Tags)
            .Include(g => g.Genres)
            .Include(g => g.GameExternalIds).ThenInclude(ge => ge.GameOffers)
            .Include(g => g.GameExternalIds).ThenInclude(ge => ge.Shop)
            .AsNoTracking();

        // Виключаємо ігри-референси з видачі
        if (refGameIds.Count > 0)
            q = q.Where(g => !refGameIds.Contains(g.GameId));

        // Жорстке виключення вже куплених ігор користувача (Owned Games)
        if (exGameIds.Count > 0) // ← НОВЕ
            q = q.Where(g => !exGameIds.Contains(g.GameId));

        // Фільтрація за небажаними тегами/жанрами
        if (excludeTagIds.Count > 0) q = q.Where(g => !g.Tags.Any(t => excludeTagIds.Contains(t.TagId)));
        if (excludeGenreIds.Count > 0) q = q.Where(g => !g.Genres.Any(gg => excludeGenreIds.Contains(gg.GenreId)));

        // Обов'язкові фільтри (AND)
        foreach (var reqTagId in requiredTagIds) { var id = reqTagId; q = q.Where(g => g.Tags.Any(t => t.TagId == id)); }
        foreach (var reqGenreId in requiredGenreIds) { var id = reqGenreId; q = q.Where(g => g.Genres.Any(gg => gg.GenreId == id)); }

        // Цінові фільтри та рейтинг
        if (isFree.HasValue)
        {
            q = isFree.Value
                ? q.Where(g => g.GameExternalIds.Any(ge => ge.GameOffers.Any(o => o.FinalPrice == 0)))
                : q.Where(g => g.GameExternalIds.Any(ge => ge.GameOffers.Any(o => o.FinalPrice > 0)));
        }

        if (minRating.HasValue) q = q.Where(g => g.Rating >= (double)minRating.Value);
        if (maxPrice.HasValue) q = q.Where(g => g.GameExternalIds.Any(ge => ge.GameOffers.Any(o => o.FinalPrice > 0 && o.FinalPrice <= maxPrice.Value)));

        if (tagMap.Count > 0 || genreMap.Count > 0)
        {
            var targetTagIds   = tagMap.Keys.ToList();
            var targetGenreIds = genreMap.Keys.ToList();
            q = q.Where(g => g.Tags.Any(t => targetTagIds.Contains(t.TagId)) || g.Genres.Any(gg => targetGenreIds.Contains(gg.GenreId)));
        }

        var candidates = await q.Take(300).ToListAsync(ct);

        // ── Крок 6: ваговий скоринг in-memory ────────────────────────────────

        const double W_TAG_BASE    = 1.0;
        const double W_TAG_BOOST   = 2.5;
        const double W_GENRE_BASE  = 1.5;
        const double W_GENRE_BOOST = 3.5;
        const double W_RATING      = 0.1;

        var scored = candidates
            .Select(g =>
            {
                var score = 0.0;
                foreach (var tag in g.Tags) if (tagMap.TryGetValue(tag.TagId, out var b)) score += b ? W_TAG_BOOST : W_TAG_BASE;
                foreach (var genre in g.Genres) if (genreMap.TryGetValue(genre.GenreId, out var b)) score += b ? W_GENRE_BOOST : W_GENRE_BASE;
                if (g.Rating > 0) score += g.Rating.Value * W_RATING;
                return (Game: g, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();

        return Ok(scored.Select(x =>
        {
            var g = x.Game;
            var bestOfferData = g.GameExternalIds
                .SelectMany(ge => ge.GameOffers.Select(o => new { Offer = o, ShopName = ge.Shop?.Name }))
                .Where(od => od.Offer.FinalPrice.HasValue)
                .MinBy(od => od.Offer.FinalPrice);

            return new
            {
                g.GameId,
                g.Name,
                Genres          = g.Genres.Select(gg => gg.Name).ToList(),
                Tags            = g.Tags.Select(gt => gt.Name).Take(12).ToList(),
                g.Rating,
                IsFree          = bestOfferData is { Offer.FinalPrice: 0 },
                BestPrice       = bestOfferData?.Offer.FinalPrice,
                Currency        = bestOfferData?.Offer.Currency,
                ShopName        = bestOfferData?.ShopName,
                SimilarityScore = Math.Round(x.Score, 2),
            };
        }));
    }

    // ── 8. Збереження векторного ембеддінгу ──────────────────────────────────

    /// <summary>
    /// Отримує згенерований Python-скриптом вектор (embedding) і зберігає його в БД для гри.
    /// </summary>
    // ── 8. Збереження векторного ембеддінгу (pgvector) ──────────────────────

    /// <summary>
    /// Отримує згенерований Python-скриптом список float і зберігає як Pgvector.Vector.
    /// </summary>
    public record SaveEmbeddingRequest(List<float> Embedding);

    [HttpPost("game/{gameId:int}/embedding")]
    public async Task<IActionResult> SaveEmbedding(
        int gameId,
        [FromBody] SaveEmbeddingRequest req,
        CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized();

        if (req.Embedding == null || req.Embedding.Count == 0)
            return BadRequest(new { error = "Вектор ембеддінгу не може бути порожнім." });

        // Шукаємо гру в базі даних
        var game = await db.Set<Game>()
            .FirstOrDefaultAsync(g => g.GameId == gameId, ct);

        if (game is null)
            return NotFound(new { error = $"Гру GameId={gameId} не знайдено." });

        // Перетворюємо List<float> з Python у структуру Pgvector.Vector
        // Якщо у вас виникне помилка компіляції, перевірте чи підключено namespace (наприклад, за допомогою new Pgvector.Vector(...))
        game.Embedding = new Pgvector.Vector(req.Embedding.ToArray()); 

        // Зберігаємо зміни в БД
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            message = $"Ембеддінг для гри #{gameId} ({game.Name}) успішно збережено в pgvector."
        });
    }

    // ── 7. Створення / оновлення цінового алерту ──────────────────────────────

    /// <summary>
    /// Створює або оновлює ціновий алерт для конкретного користувача.
    /// Агент викликає після того, як визначив цільову ціну.
    /// </summary>
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
}