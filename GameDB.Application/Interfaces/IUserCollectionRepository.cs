using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IUserCollectionRepository
{
    Task<List<UserGameListItemDto>> GetWishlistAsync(int userId, CancellationToken ct = default);
    Task<bool> IsInWishlistAsync(int userId, int gameId, CancellationToken ct = default);
    Task AddToWishlistAsync(int userId, int gameId, CancellationToken ct = default);
    Task RemoveFromWishlistAsync(int userId, int gameId, CancellationToken ct = default);
    Task<int> AddWishlistBulkAsync(int userId, IEnumerable<int> gameIds, CancellationToken ct = default);

    Task<List<UserLibraryItemDto>> GetLibraryAsync(int userId, CancellationToken ct = default);
    Task<bool> IsInLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default);
    Task AddToLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default);
    Task RemoveFromLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default);
    Task<int> AddLibraryBulkAsync(int userId, IEnumerable<int> gameIds, int shopId, CancellationToken ct = default);

    Task<List<int>> MapSteamAppIdsToGameIdsAsync(IEnumerable<int> steamAppIds, CancellationToken ct = default);
    Task<GameCollectionStateDto> GetCollectionStateAsync(int userId, int gameId, CancellationToken ct = default);
}
