using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;

namespace GameDB.Application.Interfaces;

public interface IGameEnrichmentService
{
    /// <summary>
    /// Фаза 2: Збагачує пакет ігор деталями з магазину (жанри, теги, зображення, рейтинг).
    /// Використовує вже скопований список externalIds, щоб уникнути зайвого DB-запиту.
    /// </summary>
    Task EnrichBatchAsync(
        IStoreProvider        provider,
        List<string>          externalIds,
        EnrichmentOperationState state,
        CancellationToken     ct = default);
}
