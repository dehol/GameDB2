using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameOfferRepository(AppDbContext context) : IGameOfferRepository
{
    public async Task<GameOffer?> GetGameOfferAsync(int gameId, int shopId, CancellationToken ct = default)
        => await context.GameOffers
            .FirstOrDefaultAsync(o => o.GameId == gameId && o.ShopId == shopId, ct);

    public async Task AddGameOfferAsync(GameOffer offer, CancellationToken ct = default)
    {
        await context.GameOffers.AddAsync(offer, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateGameOfferAsync(GameOffer offer, CancellationToken ct = default)
    {
        context.GameOffers.Update(offer);
        await context.SaveChangesAsync(ct);
        // Після SaveChangesAsync PostgreSQL виконає тригер fn_sync_price_history:
        // INSERT або UPDATE LastSyncedAt в PriceHistory — автоматично.
    }

    public async Task<List<GameOffer>> GetByGameIdAsync(int gameId, CancellationToken ct = default)
        => await context.GameOffers
            .AsNoTracking()
            .Include(o => o.Shop)
            .Where(o => o.GameId == gameId)
            .ToListAsync(ct);

    /// <summary>
    /// Дані для графіка ціни у SteamDB-стилі.
    ///
    /// Запит використовує LEAD() щоб обчислити кінець кожного цінового сегменту:
    ///   - для всіх записів крім останнього: PeriodEnd = RecordedAt наступного запису
    ///   - для останнього запису: PeriodEnd = LastSyncedAt (ціна актуальна до останньої синхронізації)
    ///
    /// Приклад результату для графіка (step-function):
    ///   PeriodStart          PeriodEnd            Price   Discount
    ///   2026-03-22 18:00     2026-03-29 18:00     29.99   0%
    ///   2026-03-29 18:00     2026-04-05 18:00     14.99   50%   ← знижка
    ///   2026-04-05 18:00     2026-05-26 13:09     29.99   0%    ← ціна повернулась
    /// </summary>
}