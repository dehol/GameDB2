using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IUserCollectionService
{
    Task<List<UserGameListItemDto>> GetWishlistAsync(int userId, CancellationToken ct = default);
    Task AddToWishlistAsync(int userId, int gameId, CancellationToken ct = default);
    Task RemoveFromWishlistAsync(int userId, int gameId, CancellationToken ct = default);
    Task<ImportResultDto> ImportSteamWishlistAsync(int userId, CancellationToken ct = default);

    Task<List<UserLibraryItemDto>> GetLibraryAsync(int userId, CancellationToken ct = default);
    Task AddToLibraryAsync(int userId, int gameId, int? shopId, CancellationToken ct = default);
    Task RemoveFromLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default);
    Task<ImportResultDto> ImportSteamLibraryAsync(int userId, CancellationToken ct = default);

    Task<List<AlertListItemDto>> GetAlertsAsync(int userId, CancellationToken ct = default);
    Task CreateAlertAsync(int userId, CreateAlertDto dto, CancellationToken ct = default);
    Task DeleteAlertAsync(int userId, int alertId, CancellationToken ct = default);
    Task<GameCollectionStateDto> GetCollectionStateAsync(int userId, int gameId, CancellationToken ct = default);
}
