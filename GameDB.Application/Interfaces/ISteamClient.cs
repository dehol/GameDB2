using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ISteamClient
{
    Task<IReadOnlyList<int>> GetOwnedGameAppIdsAsync(string steamId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetWishlistAppIdsAsync(string steamId, CancellationToken ct = default);
    Task<IReadOnlyCollection<SteamAppListItemDto>> GetAppListAsync(CancellationToken ct = default);
}
