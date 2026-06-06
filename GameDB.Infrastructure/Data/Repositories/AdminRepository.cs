using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    // GameId всіх ігор, що мають хоча б один GameOffer (будь-який магазин)
    private IQueryable<int> GameIdsWithPrice =>
        db.GameExternalIds
          .AsNoTracking()
          .Where(e => e.GameOffers.Any())
          .Select(e => e.GameId)
          .Distinct();

    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var gameIdsWithPrice = GameIdsWithPrice;

        var total        = await db.Games.AsNoTracking().CountAsync(ct);
        var basic        = await db.Games.AsNoTracking().CountAsync(g => g.ImportStatus == GameImportStatus.Basic, ct);
        var full         = await db.Games.AsNoTracking().CountAsync(g => g.ImportStatus == GameImportStatus.Full, ct);
        var withPrice    = await gameIdsWithPrice.CountAsync(ct);
        var basicNoPrice = await db.Games.AsNoTracking()
            .CountAsync(g => g.ImportStatus == GameImportStatus.Basic
                          && !gameIdsWithPrice.Contains(g.GameId), ct);

        var lastSync = await db.GameOffers
            .AsNoTracking()
            .MaxAsync(o => (DateTime?)o.LastSyncedAt, ct);

        return new AdminStatsDto(
            TotalGames:        total,
            StatusBasic:       basic,
            StatusFull:        full,
            WithPrice:         withPrice,
            WithoutPrice:      total - withPrice,
            BasicWithoutPrice: basicNoPrice,
            LastPriceSyncAt:   lastSync);
    }

    public Task<int> CountByStatusAsync(GameImportStatus status, CancellationToken ct = default)
        => db.Games.CountAsync(g => g.ImportStatus == status, ct);

    public async Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter,
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var q = db.Games.AsNoTracking();

        // ── Пошук ────────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (int.TryParse(term, out var gameId))
            {
                // По GameId або ExternalId з будь-якого магазину
                q = q.Where(g => g.GameId == gameId
                    || g.GameExternalIds.Any(e => e.ExternalId == term));
            }
            else
            {
                q = q.Where(g => EF.Functions.ILike(g.Name, $"%{term}%"));
            }
        }

        // ── Фільтр ────────────────────────────────────────────────────────────
        var gameIdsWithPrice = GameIdsWithPrice;

        q = filter switch
        {
            AdminGameCoverageFilter.StatusBasic  => q.Where(g => g.ImportStatus == GameImportStatus.Basic),
            AdminGameCoverageFilter.StatusFull   => q.Where(g => g.ImportStatus == GameImportStatus.Full),
            AdminGameCoverageFilter.NoPrice      => q.Where(g => !gameIdsWithPrice.Contains(g.GameId)),
            AdminGameCoverageFilter.HasPrice     => q.Where(g =>  gameIdsWithPrice.Contains(g.GameId)),
            AdminGameCoverageFilter.NoExternalId => q.Where(g => !g.GameExternalIds.Any()),
            AdminGameCoverageFilter.BasicNoPrice => q.Where(g =>
                g.ImportStatus == GameImportStatus.Basic
                && !gameIdsWithPrice.Contains(g.GameId)),
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
                g.ImportStatus,
                gameIdsWithPrice.Contains(g.GameId),
                g.GameExternalIds
                    .SelectMany(e => e.GameOffers)
                    .Max(o => (DateTime?)o.LastSyncedAt),
                g.Rating))
            .ToListAsync(ct);

        return new AdminGameListDto(items, total, page, pageSize, filter);
    }
}