using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Domain.Enums;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    public async Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var counts = await db.Games
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total        = g.Count(),
                Basic        = g.Count(x => x.ImportStatus == GameImportStatus.Basic),
                Full         = g.Count(x => x.ImportStatus == GameImportStatus.Full),
                // Витягуємо оффери через зв'язок GameExternalIds
                WithPrice    = g.Count(x => x.GameExternalIds.SelectMany(e => e.GameOffers).Any()),
                BasicNoPrice = g.Count(x => x.ImportStatus == GameImportStatus.Basic 
                                            && !x.GameExternalIds.SelectMany(e => e.GameOffers).Any()),
            })
            .FirstOrDefaultAsync(ct);

        // Separate table — still just one query
        var lastSync = await db.GameOffers
            .AsNoTracking()
            .MaxAsync(o => (DateTime?)o.LastSyncedAt, ct);

        return counts is null
            ? new AdminStatsDto(0, 0, 0, 0, 0, 0, lastSync)
            : new AdminStatsDto(
                TotalGames:        counts.Total,
                StatusBasic:       counts.Basic,
                StatusFull:        counts.Full,
                WithPrice:         counts.WithPrice,
                WithoutPrice:      counts.Total - counts.WithPrice,
                BasicWithoutPrice: counts.BasicNoPrice,
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
                // Шукаємо по GameId або по Steam ExternalId (через оновлену властивість GameExternalIds)
                q = q.Where(g => g.GameId == gameId 
                    || g.GameExternalIds.Any(e => 
                            e.ShopId == 1 && e.ExternalId == term));
            }
            else
            {
                q = q.Where(g => EF.Functions.ILike(g.Name, $"%{term}%"));
            }
        }

        // ── Фільтр ────────────────────────────────────────────────────────────
        q = filter switch
        {
            AdminGameCoverageFilter.StatusBasic   => q.Where(g => g.ImportStatus == GameImportStatus.Basic),
            AdminGameCoverageFilter.StatusFull    => q.Where(g => g.ImportStatus == GameImportStatus.Full),
            // Оновлено перевірки наявності офферів
            AdminGameCoverageFilter.NoPrice       => q.Where(g => !g.GameExternalIds.SelectMany(e => e.GameOffers).Any()),
            AdminGameCoverageFilter.HasPrice      => q.Where(g =>  g.GameExternalIds.SelectMany(e => e.GameOffers).Any()),
            // Оновлено перевірку наявності ExternalIds
            AdminGameCoverageFilter.NoExternalId  => q.Where(g => !g.GameExternalIds.Any()),
            AdminGameCoverageFilter.BasicNoPrice  => q.Where(g => 
                g.ImportStatus == GameImportStatus.Basic && !g.GameExternalIds.SelectMany(e => e.GameOffers).Any()),
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
                // Замінено ExternalIds на GameExternalIds
                g.GameExternalIds
                      .Where(e => e.ShopId == 1)
                    .Select(e => e.ExternalId)
                    .FirstOrDefault(),
                // Замінено GameOffers на GameExternalIds.SelectMany(...)
                g.GameExternalIds.SelectMany(e => e.GameOffers).Any(),
                g.GameExternalIds.SelectMany(e => e.GameOffers).Max(o => (DateTime?)o.LastSyncedAt),
                g.Rating))
            .ToListAsync(ct);

        return new AdminGameListDto(items, total, page, pageSize, filter);
    }
}