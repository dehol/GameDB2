using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var total = await db.Games.CountAsync(ct);
        var withDetails = await db.Games.CountAsync(
            g => g.Description != null && g.Description != "", ct);
        var withSteam = await db.Games.CountAsync(g => g.SteamAppId != null, ct);
        var withPrice = await db.Games.CountAsync(g => g.GameOffers.Any(), ct);
        var steamNoPrice = await db.Games.CountAsync(
            g => g.SteamAppId != null && !g.GameOffers.Any(), ct);
        var lastSync = await db.GameOffers.MaxAsync(o => (DateTime?)o.LastSyncedAt, ct);

        return new AdminStatsDto(
            TotalGames: total,
            WithDetails: withDetails,
            WithoutDetails: total - withDetails,
            WithSteamAppId: withSteam,
            WithPrice: withPrice,
            WithoutPrice: total - withPrice,
            SteamWithoutPrice: steamNoPrice,
            LastPriceSyncAt: lastSync);
    }

    public Task<int> CountWithoutDetailsAsync(CancellationToken ct = default)
        => db.Games.CountAsync(
            g => g.SteamAppId != null &&
                 (g.Description == null || g.Description == ""),
            ct);

    public async Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter,
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var q = db.Games.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (int.TryParse(term, out var gameId))
                q = q.Where(g => g.GameId == gameId || g.SteamAppId == gameId);
            else
                q = q.Where(g => EF.Functions.ILike(g.Name, $"%{term}%"));
        }

        q = filter switch
        {
            AdminGameCoverageFilter.NoDetails => q.Where(g =>
                g.Description == null || g.Description == ""),
            AdminGameCoverageFilter.HasDetails => q.Where(g =>
                g.Description != null && g.Description != ""),
            AdminGameCoverageFilter.NoPrice => q.Where(g => !g.GameOffers.Any()),
            AdminGameCoverageFilter.HasPrice => q.Where(g => g.GameOffers.Any()),
            AdminGameCoverageFilter.NoSteamAppId => q.Where(g => g.SteamAppId == null),
            AdminGameCoverageFilter.SteamNoPrice => q.Where(g =>
                g.SteamAppId != null && !g.GameOffers.Any()),
            _ => q,
        };

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(g => g.GameId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new AdminGameRowDto(
                g.GameId,
                g.Name,
                g.SteamAppId,
                g.Description != null && g.Description != "",
                g.GameOffers.Any(),
                g.GameOffers.Max(o => o.LastSyncedAt),
                g.Rating))
            .ToListAsync(ct);

        return new AdminGameListDto(items, total, page, pageSize, filter);
    }
}
