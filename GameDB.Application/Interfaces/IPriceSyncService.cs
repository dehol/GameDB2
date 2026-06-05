using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IPriceSyncService
{
    /// <summary>
    /// Фаза 3: Синхронізує ціни для пакету ігор з конкретного магазину.
    /// </summary>
    Task SyncPricesBatchAsync(
        IStoreProvider          provider,
        IReadOnlyCollection<Game> gameBatch,
        PriceSyncOperationState state,
        CancellationToken       ct = default);
}
