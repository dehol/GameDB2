using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameAlertRepository(AppDbContext db) : IGameAlertRepository
{
    public async Task<GamePriceAlertContextDto> GetPriceContextAsync(
        int gameId, int? userId, CancellationToken ct = default)
    {
        // 1. Отримуємо базову інформацію про гру
        var game = await db.Games.AsNoTracking()
            .Where(g => g.GameId == gameId)
            .Select(g => new { g.GameId, g.Name, g.HeaderImage })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Гру не знайдено.");

        // 2. Робимо запит через GameExternalIds, оскільки саме там лежать зв'язки з Shop та GameOffers
        var externalIds = await db.GameExternalIds.AsNoTracking()
            .Include(e => e.Shop)
            .Include(e => e.GameOffers)
            .Where(e => e.GameId == gameId)
            .ToListAsync(ct);

        // 3. Витягуємо (flatten) всі оффери з усіх зовнішніх джерел цієї гри в один плаский список
        var offers = externalIds
            .SelectMany(e => e.GameOffers)
            .ToList();

        var currency = offers.FirstOrDefault()?.Currency ?? "USD";

        var currentPrices = offers
            .Select(o => o.FinalPrice ?? o.CurrentPrice)
            .Where(p => p > 0)
            .ToList();

        decimal? currentLowest = currentPrices.Count > 0 ? currentPrices.Min() : null;
        decimal? basePrice     = offers.Count > 0 ? offers.Max(o => o.CurrentPrice) : null;

        var offerIds = offers.Select(o => o.GameOfferId).ToList();
        decimal? historyMin = null;
        if (offerIds.Count > 0)
        {
            historyMin = await db.PriceHistories.AsNoTracking()
                .Where(ph => offerIds.Contains(ph.GameOfferId))
                .Select(ph => (decimal?)(ph.LowestPrice ?? ph.Price))
                .MinAsync(ct);
        }

        decimal? historicalLow = historyMin;
        if (currentLowest.HasValue)
            historicalLow = historicalLow.HasValue
                ? Math.Min(historicalLow.Value, currentLowest.Value)
                : currentLowest;

        // 4. Формуємо список магазинів на основі externalIds, які мають актуальні цінові пропозиції
        var shops = externalIds
            .Where(e => e.GameOffers.Any() && e.Shop != null)
            .GroupBy(e => new { e.ShopId, e.Shop.Name })
            .Select(g => new PriceAlertShopOptionDto(g.Key.ShopId, g.Key.Name))
            .OrderBy(s => s.ShopName)
            .ToList();

        ExistingPriceAlertDto? existing = null;
        if (userId.HasValue)
        {
            var alert = await GetActiveAlertAsync(userId.Value, gameId, ct);
            if (alert is not null && alert.TargetPrice.HasValue)
            {
                existing = new ExistingPriceAlertDto(
                    alert.AlertId,
                    alert.TargetPrice.Value,
                    alert.AutoUpdate,
                    Enum.TryParse<AlertAutoUpdateMode>(alert.AutoUpdateMode, out var m)
                        ? m : AlertAutoUpdateMode.BeatLowest,
                    alert.ShopId);
            }
        }

        return new GamePriceAlertContextDto(
            game.GameId,
            game.Name,
            game.HeaderImage,
            currency,
            currentLowest,
            historicalLow,
            basePrice,
            shops,
            existing);
    }

    public Task<Alert?> GetActiveAlertAsync(int userId, int gameId, CancellationToken ct = default)
        => db.Alerts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.GameId == gameId && a.TriggeredAt == null, ct);

    public async Task AddAsync(Alert alert, CancellationToken ct = default)
    {
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Alert alert, CancellationToken ct = default)
    {
        db.Alerts.Update(alert);
        await db.SaveChangesAsync(ct);
    }

    public Task DeleteAsync(int alertId, CancellationToken ct = default)
        => db.Alerts.Where(a => a.AlertId == alertId).ExecuteDeleteAsync(ct);
}