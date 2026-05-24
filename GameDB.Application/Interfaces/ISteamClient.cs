using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ISteamClient
{
        Task<List<SteamGameData>> GetAppListAsync();
        Task<SteamAppDetailsData?> GetAppDetailsAsync(int appId);
}
