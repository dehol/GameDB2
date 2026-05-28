using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameRepository
{
    Task<Game?>          GetByIdAsync(int gameId, CancellationToken ct = default);
    Task<Game?>          GetBySteamIdAsync(int steamAppId, CancellationToken ct = default);
    Task<HashSet<int>>   GetExistingSteamAppIdsAsync(CancellationToken ct = default);
    Task<List<int>>      GetAppIdsWithoutDetailsAsync(int count, CancellationToken ct = default);
    Task<int>            GetTotalGamesCountAsync(CancellationToken ct = default);
    Task<List<Game>>     GetGamesBatchAsync(int skip, int take, CancellationToken ct = default);

    Task AddAsync(Game game, CancellationToken ct = default);
    Task BulkAddAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default);
    Task UpdateAsync(Game game, CancellationToken ct = default);
    Task DeleteAsync(int gameId, CancellationToken ct = default);
    Task DeleteBySteamIdAsync(int steamAppId, CancellationToken ct = default);

    Task<Developer> GetOrCreateDeveloperAsync(string name);
    Task<Publisher> GetOrCreatePublisherAsync(string name);
    Task<Genre> GetOrCreateGenreAsync(string name);
}