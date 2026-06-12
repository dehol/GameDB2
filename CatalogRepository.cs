using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Catalog;

public sealed class CatalogRepository(AppDbContext db) : ICatalogRepository
{
    public async Task<(List<CatalogGameDto> Items, int TotalCount)> GetPagedAsync(
        CatalogFilterDto f, CancellationToken ct = default)
    {
        var baseQuery = BuildFilteredQuery(f);

        var totalCount = await baseQuery.CountAsync(ct);

        if (totalCount == 0)
            return ([], 0);

        var rows = await ApplySorting(baseQuery, f)
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .Select(g => new
            {
                g.GameId,
                g.Name,
                g.HeaderImage,
                g.IconImage,
                g.ReleaseDate,
                g.Rating,
                g.RatingCount,

                Genres = g.Genres
                    .OrderBy(gr => gr.Name)
                    .Select(gr => gr.Name)
                    .ToList(),

                BestOffer = g.GameExternalIds
                    .SelectMany(
                        e => e.GameOffers,
                        (e, o) => new
                        {
                            o.FinalPrice,
                            o.CurrentPrice,
                            o.CurrentDiscount,
                            o.GameOfferId,
                        })
                    .OrderBy(x => x.FinalPrice ?? (decimal?)999_999m)
                    .ThenBy(x => x.GameOfferId)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new CatalogGameDto(
            GameId:           r.GameId,
            Name:             r.Name,
            HeaderImage:      r.HeaderImage,
            IconImage:        r.IconImage,
            Rating:           r.Rating,
            RatingCount:      r.RatingCount,
            Genres:           r.Genres,
            BestPrice: r.BestOffer?.CurrentPrice,
            BestDiscount:     r.BestOffer?.CurrentDiscount ?? 0,
            IsFree:           r.BestOffer?.FinalPrice == 0m
        )).ToList();

        return (items, totalCount);
    }


    private static IOrderedQueryable<Game> ApplySorting(
        IQueryable<Game> q, CatalogFilterDto f) =>
        (f.SortBy, f.SortDesc) switch
        {
            (CatalogSortBy.Name, true)
                => q.OrderByDescending(g => g.Name)
                    .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Name, false)
                => q.OrderBy(g => g.Name)
                    .ThenBy(g => g.GameId),

            (CatalogSortBy.ReleaseDate, true)
                => q.OrderByDescending(g => g.ReleaseDate)
                    .ThenByDescending(g => g.GameId),
            (CatalogSortBy.ReleaseDate, false)
                => q.OrderBy(g => g.ReleaseDate)
                    .ThenBy(g => g.GameId),

            (CatalogSortBy.Rating, true)
                => q.OrderByDescending(g => g.Rating ?? 0d)
                    .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Rating, false)
                => q.OrderBy(g => g.Rating ?? 0d)
                    .ThenBy(g => g.GameId),

            // Scalar expression — жодних JOIN і subquery
            (CatalogSortBy.Popularity, true)
                => q.OrderByDescending(g => (g.Rating ?? 0d) * (g.RatingCount ?? 0))
                    .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Popularity, false)
                => q.OrderBy(g => (g.Rating ?? 0d) * (g.RatingCount ?? 0))
                    .ThenBy(g => g.GameId),

            // Correlated MIN subquery — IX_GameOffer_ExternalId_Cover → INDEX ONLY SCAN
            (CatalogSortBy.Price, true)
                => q.OrderByDescending(g =>
                        g.GameExternalIds.SelectMany(e => e.GameOffers)
                                          .Min(o => (decimal?)o.FinalPrice) ?? 999_999m)
                    .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Price, false)
                => q.OrderBy(g =>
                        g.GameExternalIds.SelectMany(e => e.GameOffers)
                                          .Min(o => (decimal?)o.FinalPrice) ?? 999_999m)
                    .ThenBy(g => g.GameId),

            // Correlated MAX subquery — IX_GameOffer_ExternalId_Cover → INDEX ONLY SCAN
            (CatalogSortBy.Discount, true)
                => q.OrderByDescending(g =>
                        g.GameExternalIds.SelectMany(e => e.GameOffers)
                                          .Max(o => (int?)o.CurrentDiscount) ?? 0)
                    .ThenByDescending(g => g.GameId),
            (CatalogSortBy.Discount, false)
                => q.OrderBy(g =>
                        g.GameExternalIds.SelectMany(e => e.GameOffers)
                                          .Max(o => (int?)o.CurrentDiscount) ?? 0)
                    .ThenBy(g => g.GameId),

            (CatalogSortBy.UpdatedAt, true)
                => q.OrderByDescending(g => g.UpdatedAt)
                    .ThenByDescending(g => g.GameId),
            (CatalogSortBy.UpdatedAt, false)
                => q.OrderBy(g => g.UpdatedAt)
                    .ThenBy(g => g.GameId),

            _ => q.OrderByDescending(g => (g.Rating ?? 0d) * (g.RatingCount ?? 0))
                  .ThenByDescending(g => g.GameId),
        };

    // ═════════════════════════════════════════════════════════════════════════
    //  BuildFilteredQuery — бізнес-логіка фільтрів без змін
    // ═════════════════════════════════════════════════════════════════════════

    private IQueryable<Game> BuildFilteredQuery(CatalogFilterDto f)
    {
        var query = db.Games
            .AsNoTracking()
            .Where(g => g.ImportStatus == GameImportStatus.Full);

        // ── Пошук по назві ────────────────────────────────────────────────────
        // ILike генерує ILIKE — без pg_trgm це SeqScan.
        // Додайте IX_Game_Name_Trgm (GIN trigram) → перетворюється на Bitmap Index Scan.
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

    // ═════════════════════════════════════════════════════════════════════════
    //  GetSidebarDataAsync — оптимізовано: yearMin/Max → 1 query замість 2
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<CatalogSidebarDto> GetSidebarDataAsync(CancellationToken ct = default)
    {
        // Sidebar рідко змінюється — розгляньте IMemoryCache з expiry ~1h
        // на рівні Application/Service для зниження навантаження на БД.

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

        // Один запит замість двох: MIN і MAX в одному GROUP BY
        var yearRange = await db.Games
            .AsNoTracking()
            .Where(g => g.ReleaseDate != null)
            .GroupBy(_ => true)
            .Select(grp => new
            {
                Min = grp.Min(g => (int?)g.ReleaseDate!.Value.Year),
                Max = grp.Max(g => (int?)g.ReleaseDate!.Value.Year),
            })
            .FirstOrDefaultAsync(ct);

        var maxPrice = await db.GameOffers
            .AsNoTracking()
            .Where(o => o.FinalPrice != null && o.FinalPrice > 0)
            .MaxAsync(o => (decimal?)o.FinalPrice, ct) ?? 100m;

        return new CatalogSidebarDto(
            genres,
            tags,
            developers,
            publishers,
            shops,
            MinYear:  yearRange?.Min ?? 2000,
            MaxYear:  yearRange?.Max ?? DateTime.UtcNow.Year,
            MaxPrice: maxPrice);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  GetDetailAsync — projection замість entity graph (5 Include → 1 query)
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<GameDetailDto?> GetDetailAsync(int gameId, CancellationToken ct = default)
    {
        // Один SQL замість Include×5 + AsSplitQuery (5 round-trips).
        // Всі навігаційні властивості доступні у вигляді subquery/JOIN
        // прямо в проекції — EF Core 8 чудово це транслює.
        var row = await db.Games
            .AsNoTracking()
            .Where(g => g.GameId == gameId)
            .Select(g => new
            {
                g.GameId,
                g.Name,
                g.Description,
                g.HeaderImage,
                g.IconImage,
                g.ReleaseDate,
                g.Rating,
                g.RatingCount,
                g.ImportStatus,
                DeveloperName = (string?)g.Developer!.Name,
                PublisherName = (string?)g.Publisher!.Name,
                Genres = g.Genres.Select(gr => gr.Name).OrderBy(n => n).ToList(),
                Tags   = g.Tags.Select(t  => t.Name ).OrderBy(n => n).ToList(),
                // Словник slug → externalId для посилань на магазини
                ExternalIds = g.GameExternalIds
                    .Select(e => new { e.Shop.Slug, e.ExternalId })
                    .ToList(),
                // Всі офери, відсортовані за фінальною ціною
                Offers = g.GameExternalIds
                    .SelectMany(
                        e => e.GameOffers,
                        (e, o) => new
                        {
                            o.GameOfferId,
                            ShopName    = e.Shop.Name,
                            ShopBaseUrl = (string?)e.Shop.BaseUrl,
                            o.CurrentPrice,
                            o.FinalPrice,
                            Discount    = (int)o.CurrentDiscount,
                            o.Currency,
                            DownloadUrl = e.ExternalUrl,
                            o.LastSyncedAt,
                        })
                    .OrderBy(x => x.FinalPrice ?? x.CurrentPrice)
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        return new GameDetailDto(
            GameId:        row.GameId,
            Name:          row.Name,
            Description:   row.Description,
            HeaderImage:   row.HeaderImage,
            IconImage:     row.IconImage,
            ReleaseDate:   row.ReleaseDate,
            Rating:        row.Rating,
            RatingCount:   row.RatingCount,
            DeveloperName: row.DeveloperName,
            PublisherName: row.PublisherName,
            Genres:        row.Genres,
            Tags:          row.Tags,
            ExternalIds:   row.ExternalIds.ToDictionary(e => e.Slug, e => e.ExternalId),
            Offers: row.Offers.Select(o => new GameOfferDto(
                GameOfferId:  o.GameOfferId,
                ShopName:     o.ShopName,
                ShopBaseUrl:  o.ShopBaseUrl,
                CurrentPrice: o.CurrentPrice,
                FinalPrice:   o.FinalPrice,
                Discount:     o.Discount,
                Currency:     o.Currency,
                DownloadUrl:  o.DownloadUrl,
                LastSyncedAt: o.LastSyncedAt)).ToList(),
            ImportStatus: row.ImportStatus);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  GetPriceHistoryAsync — projection замість Include; WHERE Any() до Select
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<List<ShopPriceHistoryDto>> GetPriceHistoryAsync(
        int gameId, CancellationToken ct = default)
    {
        // БУЛО: Include(External+Shop) + Include(PriceHistories) → entity graph
        //       потім .Where(o => o.PriceHistories.Count > 0) у C# (пост-фільтр)
        //
        // СТАЛО: Any() у WHERE (server-side) → тільки offers з хоча б одним записом
        //        Select → projection без entity materialize
        return await db.GameOffers
            .AsNoTracking()
            .Where(o => o.External.GameId == gameId && o.PriceHistories.Any())
            .Select(o => new ShopPriceHistoryDto(
                o.GameOfferId,
                o.External.Shop.Name,
                o.PriceHistories
                    .OrderBy(ph => ph.RecordedAt)
                    .Select(ph => new PriceHistoryPointDto(
                        ph.RecordedAt,
                        ph.Price,
                        ph.DiscountPercent,
                        ph.Currency))
                    .ToList()))
            .ToListAsync(ct);
    }
}
