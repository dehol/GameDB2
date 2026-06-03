using GameDB.Domain.Entities;
using GameDB.Domain.Enums;

namespace GameDB.Application.Interfaces;

public interface IGameRepository
{
    // ── Базові CRUD ──────────────────────────────────────────────────────────
    Task<Game?>      GetByIdAsync(int gameId, CancellationToken ct = default);
    Task<Game?>      GetByExternalIdAsync(int shopId, string externalId, CancellationToken ct = default);
    Task<int>        GetTotalGamesCountAsync(CancellationToken ct = default);
    Task<List<Game>> GetGamesBatchAsync(int skip, int take, CancellationToken ct = default);
    Task<List<Game>> GetGamesBatchFromShopAsync(int skip, int take,int shopId,CancellationToken ct = default);
    Task<int>        GetGamesNotSyncedSinceCountAsync(DateTime since, CancellationToken ct = default);
    Task<List<Game>> GetGamesNotSyncedSinceBatchAsync(DateTime since, int skip, int take, CancellationToken ct = default);

    // ── Запити для імпорту ───────────────────────────────────────────────────
    Task<HashSet<string>> GetExistingExternalIdsAsync(int shopId, CancellationToken ct = default);

    /// <summary>Повертає лише ті ExternalId зі списку candidates, які вже є в БД.</summary>
    Task<HashSet<string>> GetExistingExternalIdsFromSetAsync(
        int shopId, IReadOnlyCollection<string> candidates, CancellationToken ct = default);

    Task<List<string>> GetExternalIdsByStatusAsync(int shopId, GameImportStatus status, int count, CancellationToken ct = default);
    Task<int> GetExternalIdsByStatusAsyncCount(int shopId, GameImportStatus status, CancellationToken ct = default);

    Task<List<string>> GetExternalIdsBatchAsync(int shopId, int skip, int take, CancellationToken ct = default);

    // ── Запис ────────────────────────────────────────────────────────────────
    Task AddAsync(Game game, CancellationToken ct = default);
    Task BulkAddAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default);
    Task UpdateAsync(Game game, CancellationToken ct = default);
    Task DeleteAsync(int gameId, CancellationToken ct = default);

    /// <summary>
    /// Атомарно зберігає нові ігри та нові ExternalId одним SaveChanges.
    /// Один round-trip замість N окремих AddAsync.
    /// </summary>
    Task ImportBatchAsync(
        IReadOnlyCollection<Game> newGames,
        IReadOnlyCollection<GameExternalId> newLinks,
        CancellationToken ct = default);

    // ── Lookup ───────────────────────────────────────────────────────────────
    Task<Developer> GetOrCreateDeveloperAsync(string name, CancellationToken ct = default);
    Task<Publisher> GetOrCreatePublisherAsync(string name, CancellationToken ct = default);
    Task<Genre>     GetOrCreateGenreAsync(string name, CancellationToken ct = default);
    Task<Tag>       GetOrCreateTagAsync(string name, CancellationToken ct = default);

    Task<Game?> FindByNormalizedNameAsync(string normalizedName, CancellationToken ct = default);
    Task<List<Game>> GetGamesByNormalizedNamesAsync(IEnumerable<string> names, CancellationToken ct);
    Task AddExternalIdAsync(GameExternalId externalId, CancellationToken ct = default);
}
