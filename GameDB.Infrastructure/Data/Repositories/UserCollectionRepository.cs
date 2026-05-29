using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class UserCollectionRepository(AppDbContext db) : IUserCollectionRepository
{
    public async Task<List<UserGameListItemDto>> GetWishlistAsync(int userId, CancellationToken ct = default)
    {
        var items = await db.Wishlists
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Include(w => w.Game).ThenInclude(g => g.GameOffers)
            .OrderByDescending(w => w.AddedAt)
            .ToListAsync(ct);

        return items.Select(w => MapWishlistItem(w.Game, w.AddedAt)).ToList();
    }

    public Task<bool> IsInWishlistAsync(int userId, int gameId, CancellationToken ct = default)
        => db.Wishlists.AnyAsync(w => w.UserId == userId && w.GameId == gameId, ct);

    public async Task AddToWishlistAsync(int userId, int gameId, CancellationToken ct = default)
    {
        if (await IsInWishlistAsync(userId, gameId, ct))
            return;

        db.Wishlists.Add(new Wishlist
        {
            UserId  = userId,
            GameId  = gameId,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveFromWishlistAsync(int userId, int gameId, CancellationToken ct = default)
    {
        await db.Wishlists
            .Where(w => w.UserId == userId && w.GameId == gameId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> AddWishlistBulkAsync(int userId, IEnumerable<int> gameIds, CancellationToken ct = default)
    {
        var ids = gameIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        var existing = await db.Wishlists
            .Where(w => w.UserId == userId && ids.Contains(w.GameId))
            .Select(w => w.GameId)
            .ToListAsync(ct);

        var toAdd = ids.Except(existing).Select(gid => new Wishlist
        {
            UserId  = userId,
            GameId  = gid,
            AddedAt = DateTime.UtcNow,
        }).ToList();

        if (toAdd.Count == 0) return 0;

        db.Wishlists.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
        return toAdd.Count;
    }

    public async Task<List<UserLibraryItemDto>> GetLibraryAsync(int userId, CancellationToken ct = default)
    {
        var items = await db.UserLibraries
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .Include(l => l.Game).ThenInclude(g => g.GameOffers)
            .Include(l => l.Shop)
            .OrderByDescending(l => l.AddedAt)
            .ToListAsync(ct);

        return items.Select(l => MapLibraryItem(l.Game, l.Shop, l.AddedAt)).ToList();
    }

    public Task<bool> IsInLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default)
        => db.UserLibraries.AnyAsync(
            l => l.UserId == userId && l.GameId == gameId && l.ShopId == shopId, ct);

    public async Task AddToLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default)
    {
        if (await IsInLibraryAsync(userId, gameId, shopId, ct))
            return;

        db.UserLibraries.Add(new UserLibrary
        {
            UserId  = userId,
            GameId  = gameId,
            ShopId  = shopId,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveFromLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default)
    {
        await db.UserLibraries
            .Where(l => l.UserId == userId && l.GameId == gameId && l.ShopId == shopId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> AddLibraryBulkAsync(
        int userId, IEnumerable<int> gameIds, int shopId, CancellationToken ct = default)
    {
        var ids = gameIds.Distinct().ToList();
        if (ids.Count == 0) return 0;

        var existing = await db.UserLibraries
            .Where(l => l.UserId == userId && l.ShopId == shopId && ids.Contains(l.GameId))
            .Select(l => l.GameId)
            .ToListAsync(ct);

        var toAdd = ids.Except(existing).Select(gid => new UserLibrary
        {
            UserId  = userId,
            GameId  = gid,
            ShopId  = shopId,
            AddedAt = DateTime.UtcNow,
        }).ToList();

        if (toAdd.Count == 0) return 0;

        db.UserLibraries.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
        return toAdd.Count;
    }

    public async Task<List<int>> MapSteamAppIdsToGameIdsAsync(
        IEnumerable<int> steamAppIds, CancellationToken ct = default)
    {
        var ids = steamAppIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await db.Games
            .AsNoTracking()
            .Where(g => g.SteamAppId != null && ids.Contains(g.SteamAppId.Value))
            .Select(g => g.GameId)
            .ToListAsync(ct);
    }

    public async Task<GameCollectionStateDto> GetCollectionStateAsync(
        int userId, int gameId, CancellationToken ct = default)
    {
        var inWishlist = await IsInWishlistAsync(userId, gameId, ct);

        var inLibrary = await db.UserLibraries
            .AnyAsync(l => l.UserId == userId && l.GameId == gameId, ct);

        var alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.GameId == gameId)
            .Include(a => a.Game).ThenInclude(g => g.GameOffers)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var alertDtos = alerts.Select(MapAlert).ToList();
        return new GameCollectionStateDto(inWishlist, inLibrary, alertDtos);
    }

    private static UserGameListItemDto MapWishlistItem(Game game, DateTime addedAt)
    {
        var best = game.GameOffers.OrderBy(o => o.FinalPrice ?? o.CurrentPrice).FirstOrDefault();
        return new UserGameListItemDto(
            game.GameId,
            game.Name,
            game.HeaderImage,
            game.Rating,
            best?.FinalPrice,
            best?.Currency,
            best?.CurrentDiscount ?? 0,
            addedAt,
            game.SteamAppId);
    }

    private static UserLibraryItemDto MapLibraryItem(Game game, GameShop shop, DateTime addedAt)
    {
        var best = game.GameOffers.OrderBy(o => o.FinalPrice ?? o.CurrentPrice).FirstOrDefault();
        return new UserLibraryItemDto(
            game.GameId,
            game.Name,
            game.HeaderImage,
            game.Rating,
            best?.FinalPrice,
            best?.Currency,
            best?.CurrentDiscount ?? 0,
            addedAt,
            shop.ShopId,
            shop.Name,
            game.SteamAppId);
    }

    private static AlertListItemDto MapAlert(Alert a)
    {
        var best = a.Game.GameOffers.OrderBy(o => o.FinalPrice ?? o.CurrentPrice).FirstOrDefault();
        return new AlertListItemDto(
            a.AlertId,
            a.GameId,
            a.Game.Name,
            a.Game.HeaderImage,
            a.TargetPrice,
            best?.FinalPrice,
            best?.Currency,
            a.CreatedAt,
            a.TriggeredAt,
            a.TriggeredAt is null);
    }
}
