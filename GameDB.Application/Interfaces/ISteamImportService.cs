using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ISteamImportService
{
    Task<int> ImportBasicGamesAsync();
}
