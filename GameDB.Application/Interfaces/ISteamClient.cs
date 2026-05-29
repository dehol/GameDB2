using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ISteamClient
{
    Task<List<SteamGameData>> GetAppListAsync();
    Task<SteamAppDetailsData?> GetAppDetailsAsync(int appId);
    Task<IReadOnlyList<int>> GetOwnedGameAppIdsAsync(string steamId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetWishlistAppIdsAsync(string steamId, CancellationToken ct = default);
}
