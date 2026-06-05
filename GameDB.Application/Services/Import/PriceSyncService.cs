using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Фаза 3 імпорту: синхронізує ціни для пакету ігор.
/// Зареєстрований як Scoped — кожен виклик з воркера отримує свій DbContext.
/// </summary>
public sealed class PriceSyncService(
    IPriceManagerService       priceManager,
    ILogger<PriceSyncService>  logger) : IPriceSyncService
{
    public async Task SyncPricesBatchAsync(
        IStoreProvider            provider,
        IReadOnlyCollection<Game> gameBatch,
        ImportOperationState      state,
        CancellationToken         ct = default)
    {
        // Беремо лише ExternalId для потрібного магазину
        var pairs = gameBatch
            .SelectMany(g => g.GameExternalIds
                .Where(e => e.ShopId == provider.ShopId)
                .Select(e => (Game: g, ExternalIdRecord: e)))
            .ToList();

        if (pairs.Count == 0) return;

        foreach (var (game, externalIdRecord) in pairs)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;

            try
            {
                var price = await provider.GetPriceAsync(externalIdRecord.ExternalId, ct);
                if (price is null) continue;

                await priceManager.ProcessPriceUpdateAsync(
                    externalIdRecordId: externalIdRecord.Id,
                    newPrice:           price.Price,
                    newDiscount:        price.Discount,
                    currency:           price.Currency,
                    ct:                 ct);

                state.IncrementProcessed();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                state.IncrementFailed();
                logger.LogError(ex, "[{Slug}] Помилка ціни {Id}",
                    provider.Slug, externalIdRecord.ExternalId);
            }

            await Task.Delay(provider.DelayBetweenRequestsMs, ct);
        }
    }
}
