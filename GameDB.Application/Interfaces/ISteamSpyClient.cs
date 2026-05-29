using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ISteamSpyClient
{
    Task<SteamSpyAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken ct = default);
}
