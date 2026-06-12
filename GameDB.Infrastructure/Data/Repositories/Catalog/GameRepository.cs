using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed partial class GameRepository(AppDbContext db) : IGameRepository
{
    // ── Базові запити ─────────────────────────────────────────────────────────

    public Task<Game?> GetByIdAsync(int gameId, CancellationToken ct = default)
        => db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.GameId == gameId, ct);

    public Task<int> GetGameCountByShopAsync(int shopId, CancellationToken ct = default)
        => db.Set<GameExternalId>()
            .Where(e => e.ShopId == shopId)
            .Select(e => e.GameId)
            .Distinct()
            .CountAsync(ct);

    public Task<List<Game>> GetGamesBatchFromShopAsync(int skip, int take, int shopId, CancellationToken ct = default)
        => db.Games
            .AsNoTracking()
            .Include(g => g.GameExternalIds)
                .ThenInclude(e => e.Shop)
            .Include(g => g.GameExternalIds)
                .ThenInclude(e => e.GameOffers)
            .Where(g => g.GameExternalIds.Any(e => e.ShopId == shopId))
            .OrderBy(g => g.GameId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<List<Game>> GetGamesByExternalIdsBatchAsync(
        int shopId,
        IReadOnlyCollection<string> externalIds,
        CancellationToken ct = default)
    {
        if (externalIds.Count == 0)
            return Task.FromResult(new List<Game>());

        return db.Games
            .Include(g => g.GameExternalIds)
            .Where(g => g.GameExternalIds.Any(e => e.ShopId == shopId && externalIds.Contains(e.ExternalId)))
            .ToListAsync(ct);
    }

    // ── Запити для імпорту ────────────────────────────────────────────────────

    public async Task<HashSet<string>> GetExistingExternalIdsFromSetAsync(
        int shopId, IReadOnlyCollection<string> candidates, CancellationToken ct = default)
    {
        var ids = await db.Set<GameExternalId>()
            .Where(e => e.ShopId == shopId && candidates.Contains(e.ExternalId))
            .Select(e => e.ExternalId)
            .ToListAsync(ct);
        return [..ids];
    }

    public Task<List<string>> GetExternalIdsByStatusAsync(
        int shopId, GameImportStatus status, CancellationToken ct = default)
        => db.Set<GameExternalId>()
            .Include(e => e.Game)
            .Where(e => e.ShopId == shopId && e.Game.ImportStatus == status)
            .OrderBy(e => e.GameId)
            .Select(e => e.ExternalId)
            .ToListAsync(ct);

    // ── Запис ─────────────────────────────────────────────────────────────────

    public async Task UpdateBatchAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default)
    {
        db.Games.UpdateRange(games);
        await db.SaveChangesAsync(ct);
    }

    public async Task ImportBatchAsync(
        IReadOnlyCollection<Game> newGames,
        IReadOnlyCollection<GameExternalId> newLinks,
        CancellationToken ct = default)
    {
        if (newGames.Count > 0) db.Games.AddRange(newGames);
        if (newLinks.Count > 0) db.Set<GameExternalId>().AddRange(newLinks);
        if (newGames.Count > 0 || newLinks.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    public async Task<Developer> GetOrCreateDeveloperAsync(string name, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Developer\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING", name);
        return await db.Set<Developer>().FirstAsync(d => d.Name == name, ct);
    }

    public async Task<Publisher> GetOrCreatePublisherAsync(string name, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Publisher\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING", name);
        return await db.Set<Publisher>().FirstAsync(p => p.Name == name, ct);
    }

    // ── Пошук ─────────────────────────────────────────────────────────────────

    public async Task<List<Game>> GetGamesByNormalizedNamesAsync(IEnumerable<string> names, CancellationToken ct)
        => await db.Games
            .Include(g => g.GameExternalIds)
            .Where(g => names.Contains(g.NormalizedName))
            .ToListAsync(ct);
}
