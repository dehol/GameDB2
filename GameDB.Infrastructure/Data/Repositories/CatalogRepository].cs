using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using GameDB.Infrastructure.Data;

namespace GameDB.Infrastructure.Catalog;

/// <summary>
/// Infrastructure-реалізація ICatalogRepository.
/// Всі EF-запити зосереджені тут — Application про DbContext не знає.
/// </summary>
public sealed class CatalogRepository : ICatalogRepository
{
    private readonly AppDbContext _db;

    public CatalogRepository(AppDbContext db) => _db = db;

    // ─── Каталог з фільтрами ─────────────────────────────────────────────

    public async Task<(List<CatalogGameDto> Items, int TotalCount)> GetPagedAsync(
        CatalogFilterDto f, CancellationToken ct = default)
    {
        var query = _db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Genres)
            .Include(g => g.GameOffers).ThenInclude(o => o.Shop)
            .AsQueryable();

        // ── Пошук по назві ────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim().ToLower();
            query = query.Where(g => g.Name.ToLower().Contains(s));
        }

        // ── Жанри (OR: гра містить хоча б один) ──────────────────────────
        if (f.GenreIds.Count > 0)
            query = query.Where(g => g.Genres.Any(genre => f.GenreIds.Contains(genre.GenreId)));

        // ── Розробник / Видавець / Магазин ────────────────────────────────
        if (f.DeveloperId.HasValue)
            query = query.Where(g => g.DeveloperId == f.DeveloperId.Value);
        if (f.PublisherId.HasValue)
            query = query.Where(g => g.PublisherId == f.PublisherId.Value);
        if (f.ShopId.HasValue)
            query = query.Where(g => g.GameOffers.Any(o => o.ShopId == f.ShopId.Value));

        // ── Роки випуску ──────────────────────────────────────────────────
        if (f.YearFrom.HasValue)
            query = query.Where(g => g.ReleaseDate != null && g.ReleaseDate.Value.Year >= f.YearFrom.Value);
        if (f.YearTo.HasValue)
            query = query.Where(g => g.ReleaseDate != null && g.ReleaseDate.Value.Year <= f.YearTo.Value);

        // ── Рейтинг ───────────────────────────────────────────────────────
        if (f.MinRating.HasValue)
            query = query.Where(g => g.Rating != null && g.Rating >= f.MinRating.Value);

        // ── Ціна / Знижка / Безкоштовні ──────────────────────────────────
        if (f.MinPrice.HasValue)
            query = query.Where(g => g.GameOffers.Any(o => o.FinalPrice != null && o.FinalPrice >= f.MinPrice.Value));
        if (f.MaxPrice.HasValue)
            query = query.Where(g => g.GameOffers.Any(o => o.FinalPrice != null && o.FinalPrice <= f.MaxPrice.Value));
        if (f.MinDiscount.HasValue)
            query = query.Where(g => g.GameOffers.Any(o => o.CurrentDiscount >= f.MinDiscount.Value));
        if (f.IsFree == true)
            query = query.Where(g => g.GameOffers.Any(o => o.FinalPrice == 0));

        // ── TotalCount (до пагінації) ──────────────────────────────────────
        var totalCount = await query.CountAsync(ct);

        // ── Сортування ────────────────────────────────────────────────────
        query = (f.SortBy, f.SortDesc) switch
        {
            (CatalogSortBy.Name,        true)  => query.OrderByDescending(g => g.Name),
            (CatalogSortBy.Name,        false) => query.OrderBy(g => g.Name),
            (CatalogSortBy.ReleaseDate, true)  => query.OrderByDescending(g => g.ReleaseDate),
            (CatalogSortBy.ReleaseDate, false) => query.OrderBy(g => g.ReleaseDate),
            (CatalogSortBy.Rating,      true)  => query.OrderByDescending(g => g.Rating ?? 0),
            (CatalogSortBy.Rating,      false) => query.OrderBy(g => g.Rating ?? 0),
            (CatalogSortBy.Popularity,  true)  => query.OrderByDescending(g =>
                (g.Rating ?? 0) * Math.Log10((g.RatingCount ?? 0) + 1)),
            (CatalogSortBy.Popularity,  false) => query.OrderBy(g =>
                (g.Rating ?? 0) * Math.Log10((g.RatingCount ?? 0) + 1)),
            (CatalogSortBy.Price,       true)  => query.OrderByDescending(g =>
                                                        g.GameOffers.Min(o => (decimal?)o.FinalPrice) ?? 999999),
            (CatalogSortBy.Price,       false) => query.OrderBy(g =>
                                                        g.GameOffers.Min(o => (decimal?)o.FinalPrice) ?? 999999),
            (CatalogSortBy.Discount,    true)  => query.OrderByDescending(g =>
                                                        g.GameOffers.Max(o => (int?)o.CurrentDiscount) ?? 0),
            (CatalogSortBy.Discount,    false) => query.OrderBy(g =>
                                                        g.GameOffers.Max(o => (int?)o.CurrentDiscount) ?? 0),
            (CatalogSortBy.UpdatedAt,   true)  => query.OrderByDescending(g => g.UpdatedAt),
            (CatalogSortBy.UpdatedAt,   false) => query.OrderBy(g => g.UpdatedAt),
            _                                  => query.OrderByDescending(g => g.Rating ?? 0),
        };

        // ── Пагінація ─────────────────────────────────────────────────────
        var games = await query
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .ToListAsync(ct);

        return (games.Select(MapToCard).ToList(), totalCount);
    }

    // ─── Sidebar ─────────────────────────────────────────────────────────

    public async Task<CatalogSidebarDto> GetSidebarDataAsync(CancellationToken ct = default)
    {
        var genres = await _db.Genres
            .AsNoTracking()
            .Select(g => new
            {
                g.GenreId,
                g.Name,
                GameCount = g.Games.Count()
            })
            .OrderByDescending(g => g.GameCount)
            .Take(30)
            .Select(g => new GenreFilterItemDto(g.GenreId, g.Name, g.GameCount))
            .ToListAsync(ct);

        var developers = await _db.Developers
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DeveloperFilterItemDto(d.DeveloperId, d.Name))
            .Take(200)
            .ToListAsync(ct);

        var publishers = await _db.Publishers
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PublisherFilterItemDto(p.PublisherId, p.Name))
            .Take(200)
            .ToListAsync(ct);

        var shops = await _db.GameShops
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new ShopFilterItemDto(s.ShopId, s.Name))
            .ToListAsync(ct);

        var yearMin = await _db.Games
            .Where(g => g.ReleaseDate != null)
            .MinAsync(g => (int?)g.ReleaseDate!.Value.Year, ct) ?? 2000;

        var yearMax = await _db.Games
            .Where(g => g.ReleaseDate != null)
            .MaxAsync(g => (int?)g.ReleaseDate!.Value.Year, ct) ?? DateTime.UtcNow.Year;

        var maxPrice = await _db.GameOffers
            .Where(o => o.FinalPrice != null)
            .MaxAsync(o => (decimal?)o.FinalPrice, ct) ?? 100m;

        return new CatalogSidebarDto(genres, developers, publishers, shops, yearMin, yearMax, maxPrice);
    }

    // ─── Деталі гри ──────────────────────────────────────────────────────

    public async Task<GameDetailDto?> GetDetailAsync(int gameId, CancellationToken ct = default)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .Include(g => g.GameOffers).ThenInclude(o => o.Shop)
            .FirstOrDefaultAsync(g => g.GameId == gameId, ct);

        if (game is null) return null;

        var offers = game.GameOffers
            .OrderBy(o => o.FinalPrice ?? o.CurrentPrice)
            .Select(o => new GameOfferDto(
                GameOfferId:  o.GameOfferId,
                ShopName:     o.Shop.Name,
                ShopBaseUrl:  o.Shop.BaseUrl,
                CurrentPrice: o.CurrentPrice,
                FinalPrice:   o.FinalPrice,
                Discount:     o.CurrentDiscount,
                Currency:     o.Currency,
                DownloadUrl:  o.DownloadUrl,
                LastSyncedAt: o.LastSyncedAt
            ))
            .ToList();

        return new GameDetailDto(
            GameId:        game.GameId,
            Name:          game.Name,
            Description:   game.Description,
            HeaderImage:   game.HeaderImage,
            IconImage:     game.IconImage,
            ReleaseDate:   game.ReleaseDate,
            Rating:        game.Rating,
            RatingCount:   game.RatingCount,
            DeveloperName: game.Developer?.Name,
            PublisherName: game.Publisher?.Name,
            Genres:        game.Genres.Select(g => g.Name).OrderBy(n => n).ToList(),
            Tags:          game.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
            SteamAppId:    game.SteamAppId,
            Offers:        offers
        );
    }

    // ─── Private helper ───────────────────────────────────────────────────

    private static CatalogGameDto MapToCard(Game game)
    {
        var best = game.GameOffers
            .OrderBy(o => o.FinalPrice ?? o.CurrentPrice)
            .FirstOrDefault();

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
            BestShopName:     best?.Shop.Name,
            BestDownloadUrl:  best?.DownloadUrl,
            IsFree:           best?.FinalPrice == 0m
        );
    }
}