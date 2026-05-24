using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameRepository
{
    Task<Game?> GetByIdAsync(int gameId);

     Task<Game?> GetBySlugAsync(string slug);

     Task<List<Game>> GetAllAsync();

     Task<List<Game>> GetByGenreAsync(int genreId);

     Task<HashSet<int>> GetExistingSteamAppIdsAsync();
    Task AddAsync(Game game);
    Task BulkAddAsync(List<Game> games);
    Task UpdateAsync(Game game);
    Task DeleteAsync(int gameId);

    Task<Game?> GetBySteamIdAsync(int steamAppId);
    Task<Developer> GetOrCreateDeveloperAsync(string name);
    Task<Publisher> GetOrCreatePublisherAsync(string name);
    Task<Genre> GetOrCreateGenreAsync(string steamGenreId, string name);
    Task<List<int>> GetAppIdsWithoutDetailsAsync(int count);
    Task DeleteBySteamIdAsync(int steamAppId);
}