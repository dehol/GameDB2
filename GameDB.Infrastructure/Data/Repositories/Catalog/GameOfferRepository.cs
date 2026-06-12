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

    public async Task<Dictionary<int, GameOffer>> GetBulkByExternalIdRecordsAsync(
    IReadOnlyList<int> externalIdRecordIds,
    CancellationToken ct = default)
    => await context.GameOffers
        .Where(o => externalIdRecordIds.Contains(o.ExternalId))
        .ToDictionaryAsync(o => o.ExternalId, ct);
        // EF Core генерує: SELECT ... WHERE ExternalId IN (1, 2, 3, ..., 100)
        // Один запит замість 100

    public async Task BulkUpsertAsync(
        IReadOnlyList<GameOffer> toAdd,
        IReadOnlyList<GameOffer> toUpdate,
        CancellationToken ct = default)
    {
        if (toAdd.Count > 0)
            await context.GameOffers.AddRangeAsync(toAdd, ct);

        if (toUpdate.Count > 0)
            context.GameOffers.UpdateRange(toUpdate);
            // EF Core відстежує зміни і генерує окремий UPDATE для кожного,
            // але відправляє їх усі в межах ОДНІЄЇ транзакції

        await context.SaveChangesAsync(ct);
        // Тригер fn_sync_price_history спрацьовує для кожного рядка окремо —
        // PostgreSQL row-level trigger, тому поведінка незмінна
    }
}