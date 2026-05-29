using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameRepository(AppDbContext db) : IGameRepository
{
    private static IQueryable<Game> WithSteamAppId(IQueryable<Game> q)
        => q.Where(g => g.SteamAppId != null);

    private static IQueryable<Game> WithoutMetadata(IQueryable<Game> q)
        => WithSteamAppId(q).Where(g =>
            g.DeveloperId == null ||
            !g.Genres.Any() ||
            g.Rating == null);

    public Task<Game?> GetByIdAsync(int gameId, CancellationToken ct = default)
        => db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.GameId == gameId, ct);

    public Task<Game?> GetBySteamIdAsync(int steamAppId, CancellationToken ct = default)
        => db.Games
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.SteamAppId == steamAppId, ct);

    public async Task<HashSet<int>> GetExistingSteamAppIdsAsync(CancellationToken ct = default)
    {
        var ids = await db.Games
            .Where(g => g.SteamAppId != null)
            .Select(g => g.SteamAppId!.Value)
            .ToListAsync(ct);
        return [..ids];
    }

    public Task<List<int>> GetAppIdsWithoutDetailsAsync(int count, CancellationToken ct = default)
        => WithoutMetadata(db.Games)
            .OrderBy(g => g.GameId)
            .Take(count)
            .Select(g => g.SteamAppId!.Value)
            .ToListAsync(ct);

    public Task<List<int>> GetSteamAppIdsBatchAsync(int skip, int take, CancellationToken ct = default)
        => WithSteamAppId(db.Games)
            .OrderBy(g => g.GameId)
            .Skip(skip)
            .Take(take)
            .Select(g => g.SteamAppId!.Value)
            .ToListAsync(ct);

    public Task<int> GetTotalGamesCountAsync(CancellationToken ct = default)
        => db.Games.CountAsync(ct);

    public Task<List<Game>> GetGamesBatchAsync(int skip, int take, CancellationToken ct = default)
        => db.Games.AsNoTracking()
            .Include(g => g.GameOffers)
            .OrderBy(g => g.GameId)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

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

    public async Task UpdateBatchAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default)
    {
        if (games.Count == 0) return;
        db.Games.UpdateRange(games);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int gameId, CancellationToken ct = default)
        => await db.Games.Where(g => g.GameId == gameId).ExecuteDeleteAsync(ct);

    public async Task DeleteBySteamIdAsync(int steamAppId, CancellationToken ct = default)
        => await db.Games.Where(g => g.SteamAppId == steamAppId).ExecuteDeleteAsync(ct);

    public async Task<Developer> GetOrCreateDeveloperAsync(string name)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Developer\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING",
            name).ConfigureAwait(false);

        return await db.Set<Developer>().FirstAsync(d => d.Name == name).ConfigureAwait(false);
    }

    public async Task<Publisher> GetOrCreatePublisherAsync(string name)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Publisher\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING",
            name).ConfigureAwait(false);

        return await db.Set<Publisher>().FirstAsync(p => p.Name == name).ConfigureAwait(false);
    }

    public async Task<Genre> GetOrCreateGenreAsync(string name)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Genre\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING",
            name).ConfigureAwait(false);

        return await db.Set<Genre>().FirstAsync(g => g.Name == name).ConfigureAwait(false);
    }

    public async Task<Tag> GetOrCreateTagAsync(string name)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Tag\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING",
            name).ConfigureAwait(false);

        return await db.Set<Tag>().FirstAsync(t => t.Name == name).ConfigureAwait(false);
    }
}
