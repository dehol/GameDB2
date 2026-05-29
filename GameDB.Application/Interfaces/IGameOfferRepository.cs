using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameOfferRepository
{
    Task<GameOffer?> GetGameOfferAsync(int gameId, int shopId, CancellationToken ct = default);
    Task<Dictionary<int, GameOffer>> GetOffersByGameIdsAsync(IEnumerable<int> gameIds, int shopId, CancellationToken ct = default);
    Task AddGameOfferAsync(GameOffer offer, CancellationToken ct = default);
    Task AddRangeAsync(IReadOnlyCollection<GameOffer> offers, CancellationToken ct = default);
    Task UpdateGameOfferAsync(GameOffer offer, CancellationToken ct = default);
    Task SaveBatchAsync(CancellationToken ct = default);
    Task<List<GameOffer>> GetByGameIdAsync(int gameId, CancellationToken ct = default);
}
