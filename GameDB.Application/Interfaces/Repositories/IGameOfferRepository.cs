using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameOfferRepository
{
    Task<GameOffer?> GetGameOfferAsync(int gameId, int shopId, CancellationToken ct = default);
    Task AddGameOfferAsync(GameOffer offer, CancellationToken ct = default);
    Task UpdateGameOfferAsync(GameOffer offer, CancellationToken ct = default);
    Task<List<GameOffer>> GetByGameIdAsync(int gameId, CancellationToken ct = default);

    Task<GameOffer?> GetByExternalIdRecordAsync(int externalIdRecordId, CancellationToken ct = default);

    Task<Dictionary<int, GameOffer>> GetBulkByExternalIdRecordsAsync(
    IReadOnlyList<int> externalIdRecordIds,
    CancellationToken ct = default);

    Task BulkUpsertAsync(
    IReadOnlyList<GameOffer> toAdd,
    IReadOnlyList<GameOffer> toUpdate,
    CancellationToken ct = default);
}
