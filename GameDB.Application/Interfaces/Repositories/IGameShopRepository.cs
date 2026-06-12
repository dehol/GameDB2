using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameShopRepository
{
    Task<int?> GetSteamShopIdAsync(CancellationToken ct = default);
}
