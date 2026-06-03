using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameRepository(AppDbContext db) : IGameRepository
{
    // ── CRUD ─────────────────────────────────────────────────────────────────

    public Task<Game?> GetByIdAsync(int gameId, CancellationToken ct = default)
        => db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.GameId == gameId, ct);

    public Task<Game?> GetByExternalIdAsync(int shopId, string externalId, CancellationToken ct = default)
        => db.Games
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .Include(g => g.ExternalIds)
            .FirstOrDefaultAsync(g =>
                g.ExternalIds.Any(e => e.ShopId == shopId && e.ExternalId == externalId), ct);

    public Task<int> GetTotalGamesCountAsync(CancellationToken ct = default)
        => db.Games.CountAsync(ct);

    public Task<List<Game>> GetGamesBatchAsync(int skip, int take, CancellationToken ct = default)
        => db.Games.AsNoTracking()
            .Include(g => g.GameOffers)
            .Include(g => g.ExternalIds)
            .OrderBy(g => g.GameId)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
    public Task<List<Game>> GetGamesBatchFromShopAsync(int skip, int take,int shopId,CancellationToken ct = default)
        =>  db.Games
            .AsNoTracking()
            .Include(g => g.GameOffers)
            .Include(g => g.ExternalIds)
                .ThenInclude(e => e.Shop)
            .Where(g => g.ExternalIds.Any(e => e.ShopId == shopId))
            .OrderBy(g => g.GameId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        

    // ── Not-synced-since ─────────────────────────────────────────────────────

    private IQueryable<Game> QueryNotSyncedSince(DateTime since)
    {
        var sinceUtc = since.Kind == DateTimeKind.Utc
            ? since : DateTime.SpecifyKind(since, DateTimeKind.Utc);
        return db.Games.AsNoTracking()
            .Where(g => !g.GameOffers.Any(o => o.LastSyncedAt != null && o.LastSyncedAt >= sinceUtc));
    }

    public Task<int> GetGamesNotSyncedSinceCountAsync(DateTime since, CancellationToken ct = default)
        => QueryNotSyncedSince(since).CountAsync(ct);

    public Task<List<Game>> GetGamesNotSyncedSinceBatchAsync(DateTime since, int skip, int take, CancellationToken ct = default)
        => QueryNotSyncedSince(since)
            .Include(g => g.GameOffers)
            .Include(g => g.ExternalIds)
            .OrderBy(g => g.GameId)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    // ── Import queries ────────────────────────────────────────────────────────

    public async Task<HashSet<string>> GetExistingExternalIdsAsync(int shopId, CancellationToken ct = default)
    {
        var ids = await db.Set<GameExternalId>()
            .Where(e => e.ShopId == shopId).Select(e => e.ExternalId).ToListAsync(ct);
        return [..ids];
    }

    public async Task<HashSet<string>> GetExistingExternalIdsFromSetAsync(
        int shopId, IReadOnlyCollection<string> candidates, CancellationToken ct = default)
    {
        var ids = await db.Set<GameExternalId>()
            .Where(e => e.ShopId == shopId && candidates.Contains(e.ExternalId))
            .Select(e => e.ExternalId).ToListAsync(ct);
        return [..ids];
    }

    public Task<List<string>> GetExternalIdsByStatusAsync(
        int shopId, GameImportStatus status, int count, CancellationToken ct = default)
        => db.Set<GameExternalId>()
            .Include(e => e.Game)
            .Where(e => e.ShopId == shopId && e.Game.ImportStatus == status)
            .OrderBy(e => e.GameId).Take(count)
            .Select(e => e.ExternalId).ToListAsync(ct);
    public Task<int> GetExternalIdsByStatusAsyncCount(
        int shopId, GameImportStatus status, CancellationToken ct = default)
        => db.Set<GameExternalId>()
            .Include(e => e.Game)
            .Where(e => e.ShopId == shopId && e.Game.ImportStatus == status)
            .CountAsync(ct);

    public Task<List<string>> GetExternalIdsBatchAsync(
        int shopId, int skip, int take, CancellationToken ct = default)
        => db.Set<GameExternalId>()
            .Where(e => e.ShopId == shopId)
            .OrderBy(e => e.GameId).Skip(skip).Take(take)
            .Select(e => e.ExternalId).ToListAsync(ct);

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task AddAsync(Game game, CancellationToken ct = default)
    {
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
    }

    public async Task BulkAddAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default)
    {
        db.Games.AddRange(games);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Game game, CancellationToken ct = default)
    {
        db.Games.Update(game);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int gameId, CancellationToken ct = default)
        => await db.Games.Where(g => g.GameId == gameId).ExecuteDeleteAsync(ct);

    /// <summary>
    /// Один SaveChanges на весь батч — суттєво знижує кількість round-trips до БД.
    /// </summary>
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

    public async Task<Genre> GetOrCreateGenreAsync(string name, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Genre\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING", name);
        return await db.Set<Genre>().FirstAsync(g => g.Name == name, ct);
    }

    public async Task<Tag> GetOrCreateTagAsync(string name, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Tag\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING", name);
        return await db.Set<Tag>().FirstAsync(t => t.Name == name, ct);
    }

    public Task<Game?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct = default)
        => db.Games.Include(g => g.ExternalIds)
            .FirstOrDefaultAsync(g => g.NormalizedName == normalizedName, ct);

    public async Task<List<Game>> GetGamesByNormalizedNamesAsync(IEnumerable<string> names, CancellationToken ct)
        => await db.Games.Include(g => g.ExternalIds)
            .Where(g => names.Contains(g.NormalizedName)).ToListAsync(ct);

    public async Task AddExternalIdAsync(GameExternalId externalId, CancellationToken ct = default)
    {
        db.Set<GameExternalId>().Add(externalId);
        await db.SaveChangesAsync(ct);
    }
}
