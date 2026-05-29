using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameOfferRepository(AppDbContext context) : IGameOfferRepository
{
    public async Task<GameOffer?> GetGameOfferAsync(int gameId, int shopId, CancellationToken ct = default)
        => await context.GameOffers
            .FirstOrDefaultAsync(o => o.GameId == gameId && o.ShopId == shopId, ct);

    public async Task<Dictionary<int, GameOffer>> GetOffersByGameIdsAsync(
        IEnumerable<int> gameIds,
        int shopId,
        CancellationToken ct = default)
    {
        var ids = gameIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var offers = await context.GameOffers
            .Where(o => o.ShopId == shopId && ids.Contains(o.GameId))
            .ToListAsync(ct);

        return offers.ToDictionary(o => o.GameId);
    }

    public async Task AddGameOfferAsync(GameOffer offer, CancellationToken ct = default)
    {
        await context.GameOffers.AddAsync(offer, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IReadOnlyCollection<GameOffer> offers, CancellationToken ct = default)
    {
        if (offers.Count == 0) return;
        await context.GameOffers.AddRangeAsync(offers, ct);
    }

    public async Task UpdateGameOfferAsync(GameOffer offer, CancellationToken ct = default)
    {
        context.GameOffers.Update(offer);
        await context.SaveChangesAsync(ct);
    }

    public async Task SaveBatchAsync(CancellationToken ct = default)
        => await context.SaveChangesAsync(ct);

    public async Task<List<GameOffer>> GetByGameIdAsync(int gameId, CancellationToken ct = default)
        => await context.GameOffers
            .AsNoTracking()
            .Include(o => o.Shop)
            .Where(o => o.GameId == gameId)
            .ToListAsync(ct);
}
