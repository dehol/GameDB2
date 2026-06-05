using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Catalog;

public sealed class CatalogRepository(AppDbContext db) : ICatalogRepository
{
    // ─── Каталог з фільтрами ─────────────────────────────────────────────────

    public async Task<(List<CatalogGameDto> Items, int TotalCount)> GetPagedAsync(
        CatalogFilterDto f, CancellationToken ct = default)
    {
        // Крок 1: Базовий запит (лише фільтри, без Include).
        // Використовується для CountAsync — чистий SELECT COUNT(*) без JOIN-вибухів.
        var baseQuery = BuildFilteredQuery(f);

        // Крок 2: Точний підрахунок (без Include → EF не приєднує колекції до COUNT).
        var totalCount = await baseQuery.CountAsync(ct);

        // Крок 3: Сортування зі СТАБІЛЬНИМ вторинним ключем GameId.
        // ⚠️ КРИТИЧНО: без ThenBy(GameId) SQL-сервер повертає рядки в довільному
        // порядку при однаковому основному значенні (Rating=null → 0*0=0 для всіх).
        // Це спричиняє перекривання сторінок: одні ігри з'являються двічі, інші
        // не з'являються взагалі — саме тому пагінація "не працювала".
        IOrderedQueryable<Game> sorted = (f.SortBy, f.SortDesc) switch
        {
            (CatalogSortBy.Name,        true)  => baseQuery.OrderByDescending(g => g.Name)
                                                            .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Name,        false) => baseQuery.OrderBy(g => g.Name)
                                                            .ThenBy(g => g.GameId),

            (CatalogSortBy.ReleaseDate, true)  => baseQuery.OrderByDescending(g => g.ReleaseDate)
                                                            .ThenByDescending(g => g.GameId),
            (CatalogSortBy.ReleaseDate, false) => baseQuery.OrderBy(g => g.ReleaseDate)
                                                            .ThenBy(g => g.GameId),

            (CatalogSortBy.Rating,      true)  => baseQuery.OrderByDescending(g => g.Rating ?? 0)
                                                            .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Rating,      false) => baseQuery.OrderBy(g => g.Rating ?? 0)
                                                            .ThenBy(g => g.GameId),

            (CatalogSortBy.Popularity,  true)  => baseQuery.OrderByDescending(g => (g.Rating ?? 0) * (g.RatingCount ?? 0))
                                                            .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Popularity,  false) => baseQuery.OrderBy(g => (g.Rating ?? 0) * (g.RatingCount ?? 0))
                                                            .ThenBy(g => g.GameId),

            (CatalogSortBy.Price,       true)  => baseQuery.OrderByDescending(
                                                        g => g.GameExternalIds
                                                               .SelectMany(e => e.GameOffers)
                                                               .Min(o => (decimal?)o.FinalPrice) ?? 999999m)
                                                            .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Price,       false) => baseQuery.OrderBy(
                                                        g => g.GameExternalIds
                                                               .SelectMany(e => e.GameOffers)
                                                               .Min(o => (decimal?)o.FinalPrice) ?? 999999m)
                                                            .ThenBy(g => g.GameId),

            (CatalogSortBy.Discount,    true)  => baseQuery.OrderByDescending(
                                                        g => g.GameExternalIds
                                                               .SelectMany(e => e.GameOffers)
                                                               .Max(o => (int?)o.CurrentDiscount) ?? 0)
                                                            .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Discount,    false) => baseQuery.OrderBy(
                                                        g => g.GameExternalIds
                                                               .SelectMany(e => e.GameOffers)
                                                               .Max(o => (int?)o.CurrentDiscount) ?? 0)
                                                            .ThenBy(g => g.GameId),

            (CatalogSortBy.UpdatedAt,   true)  => baseQuery.OrderByDescending(g => g.UpdatedAt)
                                                            .ThenByDescending(g => g.GameId),
            (CatalogSortBy.UpdatedAt,   false) => baseQuery.OrderBy(g => g.UpdatedAt)
                                                            .ThenBy(g => g.GameId),

            _                                  => baseQuery.OrderByDescending(g => (g.Rating ?? 0) * (g.RatingCount ?? 0))
                                                            .ThenByDescending(g => g.GameId),
        };

        // Крок 4: Пагінація + Include + AsSplitQuery.
        // AsSplitQuery ставимо тут (не на початку), щоб COUNT вже було виконано.
        // Skip/Take застосовується до відфільтрованого/відсортованого набору,
        // а Include завантажує пов'язані дані лише для 24 (PageSize) ігор.
        var games = await sorted
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .Include(g => g.Developer)
            .Include(g => g.Genres)
            .Include(g => g.GameExternalIds).ThenInclude(e => e.GameOffers)
            .Include(g => g.GameExternalIds).ThenInclude(e => e.Shop)
            .AsSplitQuery()
            .ToListAsync(ct);

        return (games.Select(MapToCard).ToList(), totalCount);
    }

    // ─── Фільтри (виділено окремо, щоб повторно використати для COUNT) ────────

    private IQueryable<Game> BuildFilteredQuery(CatalogFilterDto f)
    {
        var query = db.Games
            .AsNoTracking()
            .Where(g => g.ImportStatus == GameImportStatus.Full);

        // ── Пошук по назві ────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query = query.Where(g => EF.Functions.ILike(g.Name, $"%{s}%"));
        }

        // ── Жанри / Теги ──────────────────────────────────────────────────────
        if (f.GenreIds.Count > 0)
            query = query.Where(g => g.Genres.Any(genre => f.GenreIds.Contains(genre.GenreId)));

        if (f.TagIds.Count > 0)
            query = query.Where(g => g.Tags.Any(tag => f.TagIds.Contains(tag.TagId)));

        // ── Розробник / Видавець / Магазин ────────────────────────────────────
        if (f.DeveloperId.HasValue)
            query = query.Where(g => g.DeveloperId == f.DeveloperId.Value);
        if (f.PublisherId.HasValue)
            query = query.Where(g => g.PublisherId == f.PublisherId.Value);

        if (f.ShopId.HasValue)
            query = query.Where(g =>
                g.GameExternalIds.Any(e => e.ShopId == f.ShopId.Value && e.GameOffers.Any()));

        // ── Роки випуску ──────────────────────────────────────────────────────
        if (f.YearFrom.HasValue)
            query = query.Where(g => g.ReleaseDate != null
                                     && g.ReleaseDate.Value.Year >= f.YearFrom.Value);
        if (f.YearTo.HasValue)
            query = query.Where(g => g.ReleaseDate != null
                                     && g.ReleaseDate.Value.Year <= f.YearTo.Value);

        // ── Рейтинг ───────────────────────────────────────────────────────────
        if (f.MinRating.HasValue)
            query = query.Where(g => g.Rating != null && g.Rating >= f.MinRating.Value);

        // ── Ціна / Знижка / Безкоштовні ───────────────────────────────────────
        if (f.MinPrice.HasValue)
            query = query.Where(g =>
                g.GameExternalIds.Any(e => e.GameOffers.Any(o => o.FinalPrice >= f.MinPrice.Value)));
        if (f.MaxPrice.HasValue)
            query = query.Where(g =>
                g.GameExternalIds.Any(e => e.GameOffers.Any(o => o.FinalPrice <= f.MaxPrice.Value)));
        if (f.MinDiscount.HasValue)
            query = query.Where(g =>
                g.GameExternalIds.Any(e => e.GameOffers.Any(o => o.CurrentDiscount >= f.MinDiscount.Value)));
        if (f.IsFree == true)
            query = query.Where(g =>
                g.GameExternalIds.Any(e => e.GameOffers.Any(o => o.FinalPrice == 0)));

        return query;
    }

    // ─── Sidebar ─────────────────────────────────────────────────────────────

    public async Task<CatalogSidebarDto> GetSidebarDataAsync(CancellationToken ct = default)
    {
        var genres = await db.Genres
            .AsNoTracking()
            .Select(g => new { g.GenreId, g.Name, GameCount = g.Games.Count() })
            .OrderByDescending(g => g.GameCount)
            .Take(30)
            .Select(g => new GenreFilterItemDto(g.GenreId, g.Name, g.GameCount))
            .ToListAsync(ct);

        var tags = await db.Tags
            .AsNoTracking()
            .Select(t => new { t.TagId, t.Name, GameCount = t.Games.Count() })
            .OrderByDescending(t => t.GameCount)
            .Take(30)
            .Select(t => new TagFilterItemDto(t.TagId, t.Name, t.GameCount))
            .ToListAsync(ct);

        var developers = await db.Developers
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DeveloperFilterItemDto(d.DeveloperId, d.Name))
            .Take(200)
            .ToListAsync(ct);

        var publishers = await db.Publishers
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PublisherFilterItemDto(p.PublisherId, p.Name))
            .Take(200)
            .ToListAsync(ct);

        var shops = await db.GameShops
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new ShopFilterItemDto(s.ShopId, s.Name))
            .ToListAsync(ct);

        var yearMin = await db.Games
            .Where(g => g.ReleaseDate != null)
            .MinAsync(g => (int?)g.ReleaseDate!.Value.Year, ct) ?? 2000;

        var yearMax = await db.Games
            .Where(g => g.ReleaseDate != null)
            .MaxAsync(g => (int?)g.ReleaseDate!.Value.Year, ct) ?? DateTime.UtcNow.Year;

        var maxPrice = await db.GameOffers
            .Where(o => o.FinalPrice != null)
            .MaxAsync(o => (decimal?)o.FinalPrice, ct) ?? 100m;

        return new CatalogSidebarDto(genres, tags, developers, publishers, shops, yearMin, yearMax, maxPrice);
    }

    // ─── Деталі гри ──────────────────────────────────────────────────────────

    public async Task<GameDetailDto?> GetDetailAsync(int gameId, CancellationToken ct = default)
    {
        var game = await db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .Include(g => g.GameExternalIds).ThenInclude(e => e.Shop)
            .Include(g => g.GameExternalIds).ThenInclude(e => e.GameOffers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.GameId == gameId, ct);

        if (game is null) return null;

        var offers = game.GameExternalIds
            .SelectMany(e => e.GameOffers.Select(o => new { External = e, Offer = o }))
            .OrderBy(x => x.Offer.FinalPrice ?? x.Offer.CurrentPrice)
            .Select(x => new GameOfferDto(
                GameOfferId:  x.Offer.GameOfferId,
                ShopName:     x.External.Shop?.Name ?? "Unknown",
                ShopBaseUrl:  x.External.Shop?.BaseUrl,
                CurrentPrice: x.Offer.CurrentPrice,
                FinalPrice:   x.Offer.FinalPrice,
                Discount:     x.Offer.CurrentDiscount,
                Currency:     x.Offer.Currency,
                DownloadUrl:  x.External.ExternalUrl,
                LastSyncedAt: x.Offer.LastSyncedAt))
            .ToList();

        var externalIds = game.GameExternalIds
            .Where(e => e.Shop is not null)
            .ToDictionary(e => e.Shop!.Slug, e => e.ExternalId);

        return new GameDetailDto(
            GameId:        game.GameId,
            Name:          game.Name,
            HeaderImage:   game.HeaderImage,
            IconImage:     game.IconImage,
            ReleaseDate:   game.ReleaseDate,
            Rating:        game.Rating,
            RatingCount:   game.RatingCount,
            DeveloperName: game.Developer?.Name,
            PublisherName: game.Publisher?.Name,
            Genres:        game.Genres.Select(g => g.Name).OrderBy(n => n).ToList(),
            Tags:          game.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
            ExternalIds:   externalIds,
            Offers:        offers,
            ImportStatus:  game.ImportStatus);
    }

    // ─── Private helper ───────────────────────────────────────────────────────

    private static CatalogGameDto MapToCard(Game game)
    {
        var bestPair = game.GameExternalIds
            .SelectMany(e => e.GameOffers.Select(o => new { External = e, Offer = o }))
            .OrderBy(x => x.Offer.FinalPrice ?? x.Offer.CurrentPrice)
            .FirstOrDefault();

        var bestExt = bestPair?.External;
        var best    = bestPair?.Offer;

        return new CatalogGameDto(
            GameId:           game.GameId,
            Name:             game.Name,
            HeaderImage:      game.HeaderImage,
            IconImage:        game.IconImage,
            ReleaseDate:      game.ReleaseDate,
            Rating:           game.Rating,
            RatingCount:      game.RatingCount,
            DeveloperName:    game.Developer?.Name,
            Genres:           game.Genres.Select(g => g.Name).OrderBy(n => n).ToList(),
            BestFinalPrice:   best?.FinalPrice,
            BestCurrentPrice: best?.CurrentPrice,
            BestDiscount:     best?.CurrentDiscount ?? 0,
            BestCurrency:     best?.Currency,
            BestShopName:     bestExt?.Shop?.Name,
            BestDownloadUrl:  bestExt?.ExternalUrl,
            IsFree:           best?.FinalPrice == 0m);
    }
}
