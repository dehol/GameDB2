using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameOfferRepository(AppDbContext context) : IGameOfferRepository
{
    /// <summary>
    /// Знаходить GameOffer через GameExternalId (GameId + ShopId).
    /// Використовується коли маємо тільки gameId/shopId, але не знаємо ExternalId.Id.
    /// </summary>
    public async Task<GameOffer?> GetGameOfferAsync(int gameId, int shopId, CancellationToken ct = default)
        => await context.GameOffers
            .Include(o => o.External)
            .FirstOrDefaultAsync(o => o.External.GameId == gameId && o.External.ShopId == shopId, ct);

    /// <summary>
    /// Знаходить GameOffer безпосередньо по FK (GameExternalId.Id).
    /// Швидший шлях — використовується в PriceManagerService.
    /// </summary>
    public async Task<GameOffer?> GetByExternalIdRecordAsync(int externalIdRecordId, CancellationToken ct = default)
        => await context.GameOffers
            .FirstOrDefaultAsync(o => o.ExternalId == externalIdRecordId, ct);

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

    /// <summary>
    /// Усі оффери для конкретної гри (через GameExternalId).
    /// Включає Shop для відображення назви магазину.
    /// </summary>
    public async Task<List<GameOffer>> GetByGameIdAsync(int gameId, CancellationToken ct = default)
        => await context.GameOffers
            .AsNoTracking()
            .Include(o => o.External)
                .ThenInclude(e => e.Shop)
            .Where(o => o.External.GameId == gameId)
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