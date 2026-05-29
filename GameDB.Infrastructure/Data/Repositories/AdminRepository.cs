using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    private static IQueryable<Domain.Entities.Game> WithMetadata(IQueryable<Domain.Entities.Game> q)
        => q.Where(g => g.SteamAppId != null &&
                        g.DeveloperId != null &&
                        g.Genres.Any() &&
                        g.Rating != null);

    private static IQueryable<Domain.Entities.Game> WithoutMetadata(IQueryable<Domain.Entities.Game> q)
        => q.Where(g => g.SteamAppId != null &&
                        (g.DeveloperId == null ||
                         !g.Genres.Any() ||
                         g.Rating == null));

    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var total = await db.Games.CountAsync(ct);
        var withDetails = await WithMetadata(db.Games).CountAsync(ct);
        var withoutDetails = await WithoutMetadata(db.Games).CountAsync(ct);
        var withSteam = await db.Games.CountAsync(g => g.SteamAppId != null, ct);
        var withPrice = await db.Games.CountAsync(g => g.GameOffers.Any(), ct);
        var steamNoPrice = await db.Games.CountAsync(
            g => g.SteamAppId != null && !g.GameOffers.Any(), ct);
        var lastSync = await db.GameOffers.MaxAsync(o => (DateTime?)o.LastSyncedAt, ct);

        return new AdminStatsDto(
            TotalGames: total,
            WithDetails: withDetails,
            WithoutDetails: withoutDetails,
            WithSteamAppId: withSteam,
            WithPrice: withPrice,
            WithoutPrice: total - withPrice,
            SteamWithoutPrice: steamNoPrice,
            LastPriceSyncAt: lastSync);
    }

    public Task<int> CountWithoutDetailsAsync(CancellationToken ct = default)
        => WithoutMetadata(db.Games).CountAsync(ct);

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
            AdminGameCoverageFilter.NoDetails => WithoutMetadata(q),
            AdminGameCoverageFilter.HasDetails => WithMetadata(q),
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
                g.SteamAppId != null &&
                    g.DeveloperId != null &&
                    g.Genres.Any() &&
                    g.Rating != null,
                g.GameOffers.Any(),
                g.GameOffers.Max(o => o.LastSyncedAt),
                g.Rating))
            .ToListAsync(ct);

        return new AdminGameListDto(items, total, page, pageSize, filter);
    }
}
