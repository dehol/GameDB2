// GameRepository.cs
using EFCore.BulkExtensions;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public class GameRepository : IGameRepository
{
    private readonly AppDbContext _db;

    public GameRepository(AppDbContext db) => _db = db;

    public async Task<Game?> GetByIdAsync(int gameId)
        => await _db.Games.FindAsync(gameId);

    public async Task<Game?> GetBySlugAsync(string slug)
        => await _db.Games.FirstOrDefaultAsync(g => g.Slug == slug);

    public async Task<List<Game>> GetAllAsync()
        => await _db.Games.ToListAsync();

    public async Task<List<Game>> GetByGenreAsync(int genreId)
        => await _db.Games
            .Include(g => g.Genres)
            .Where(g => g.Genres.Any(gen => gen.GenreId == genreId))
            .ToListAsync();

    public async Task<HashSet<int>> GetExistingSteamAppIdsAsync()
        => new HashSet<int>(
            await _db.Games
                .Where(g => g.SteamAppId.HasValue)
                .Select(g => g.SteamAppId.Value)
                .ToListAsync()
        );

    public async Task AddAsync(Game game)
    {
        _db.Games.Add(game);
        await _db.SaveChangesAsync();
    }

    public async Task BulkAddAsync(List<Game> games)
    {
        await _db.BulkInsertAsync(games);
    }

    public async Task UpdateAsync(Game game)
    {
        _db.Games.Update(game);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int gameId)
    {
        await _db.Games
            .Where(g => g.GameId == gameId)
            .ExecuteDeleteAsync();
    }

    public async Task<Game?> GetBySteamIdAsync(int steamAppId)
        => await _db.Games
            .Include(g => g.Genres)
            .FirstOrDefaultAsync(g => g.SteamAppId == steamAppId);

    public async Task<Developer> GetOrCreateDeveloperAsync(string name)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Developer\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING",
            name);

        return await _db.Set<Developer>().FirstAsync(d => d.Name == name);
    }

    public async Task<Publisher> GetOrCreatePublisherAsync(string name)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Publisher\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING",
            name);

        return await _db.Set<Publisher>().FirstAsync(p => p.Name == name);
    }

    public async Task<Genre> GetOrCreateGenreAsync(string steamGenreId, string name)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"Genre\" (\"Name\") VALUES ({0}) ON CONFLICT (\"Name\") DO NOTHING",
            name);

        return await _db.Set<Genre>().FirstAsync(g => g.Name == name);
    }

    public async Task<List<int>> GetAppIdsWithoutDetailsAsync(int count)
    {
        return await _db.Games
            .Where(g => g.SteamAppId != null && g.Description == null)
            .OrderBy(g => g.GameId)
            .Select(g => g.SteamAppId.Value)
            .Take(count)
            .ToListAsync();
    }

    public async Task DeleteBySteamIdAsync(int steamAppId)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.SteamAppId == steamAppId);
        if (game != null)
        {
            _db.Games.Remove(game);
            await _db.SaveChangesAsync();
        }
    }
}