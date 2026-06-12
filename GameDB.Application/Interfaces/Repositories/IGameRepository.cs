using GameDB.Domain.Entities;
using GameDB.Domain.Enums;

namespace GameDB.Application.Interfaces;

public interface IGameRepository
{
    // ── Базові запити ─────────────────────────────────────────────────────────

    Task<Game?>      GetByIdAsync(int gameId, CancellationToken ct = default);
    Task<int>        GetGameCountByShopAsync(int shopId, CancellationToken ct = default);
    Task<List<Game>> GetGamesBatchFromShopAsync(int skip, int take, int shopId, CancellationToken ct = default);
    Task<List<Game>> GetGamesByExternalIdsBatchAsync(int shopId, IReadOnlyCollection<string> externalIds, CancellationToken ct = default);

    // ── Запити для імпорту ────────────────────────────────────────────────────

    /// <summary>Повертає лише ті ExternalId зі списку candidates, які вже є в БД.</summary>
    Task<HashSet<string>> GetExistingExternalIdsFromSetAsync(
        int shopId, IReadOnlyCollection<string> candidates, CancellationToken ct = default);

    Task<List<string>> GetExternalIdsByStatusAsync(int shopId, GameImportStatus status, CancellationToken ct = default);

    // ── Запис ─────────────────────────────────────────────────────────────────

    Task UpdateBatchAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default);

    /// <summary>
    /// Атомарно зберігає нові ігри та нові ExternalId одним SaveChanges.
    /// </summary>
    Task ImportBatchAsync(
        IReadOnlyCollection<Game>           newGames,
        IReadOnlyCollection<GameExternalId> newLinks,
        CancellationToken                   ct = default);

    // ── Lookup ────────────────────────────────────────────────────────────────

    Task<Developer> GetOrCreateDeveloperAsync(string name, CancellationToken ct = default);
    Task<Publisher> GetOrCreatePublisherAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Upsert усіх жанрів одним SQL, повертає словник name → Genre.
    /// </summary>
    Task<Dictionary<string, Genre>> GetOrCreateGenresBulkAsync(
        IReadOnlyCollection<string> names, CancellationToken ct = default);

    /// <summary>
    /// Upsert усіх тегів одним SQL, повертає словник name → Tag.
    /// </summary>
    Task<Dictionary<string, Tag>> GetOrCreateTagsBulkAsync(
        IReadOnlyCollection<string> names, CancellationToken ct = default);

    // ── Пошук ─────────────────────────────────────────────────────────────────

    Task<List<Game>> GetGamesByNormalizedNamesAsync(IEnumerable<string> names, CancellationToken ct);
}
