using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ISteamSpyClient
{
    Task<SteamSpyAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken ct = default);
    Task<IReadOnlyCollection<SteamSpyAppListItemDto>> GetAppListPageAsync(int page, CancellationToken ct = default);
    Task<IReadOnlyCollection<SteamSpyAppListItemDto>> GetAllAppsAsync(CancellationToken ct = default);
}
