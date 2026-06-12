using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameShopRepository(AppDbContext db) : IGameShopRepository
{
    public async Task<int?> GetSteamShopIdAsync(CancellationToken ct = default)
    {
        var shop = await db.GameShops
            .AsNoTracking()
            .Where(s => EF.Functions.ILike(s.Name, "%steam%"))
            .OrderBy(s => s.ShopId)
            .FirstOrDefaultAsync(ct);

        return shop?.ShopId;
    }
}
