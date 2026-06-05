using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Оновлює GameOffer при синхронізації ціни.
/// PriceHistory керується автоматично тригером fn_sync_price_history:
///   - ціна змінилась → тригер INSERT новий PriceHistory з RecordedAt = NOW()
///   - ціна та сама  → тригер UPDATE LastSyncedAt = NOW() на поточному рядку
///
/// Moved: Services/ → Services/Import/ (узгодженість з пайплайном імпорту)
/// Fix: реєструється через IPriceManagerService (раніше — як concrete type, порушення DIP)
/// </summary>
public sealed class PriceManagerService(
    IGameOfferRepository          offerRepository,
    ILogger<PriceManagerService>  logger) : IPriceManagerService
{
    public async Task ProcessPriceUpdateAsync(
        int               externalIdRecordId,
        decimal           newPrice,
        short             newDiscount,
        string            currency,
        CancellationToken ct = default)
    {
        var offer = await offerRepository.GetByExternalIdRecordAsync(externalIdRecordId, ct);

        if (offer is null)
        {
            // Тригер trg_sync_price_history (AFTER INSERT) вставить перший PriceHistory.
            var newOffer = new GameOffer
            {
                ExternalId      = externalIdRecordId,
                CurrentPrice    = newPrice,
                CurrentDiscount = newDiscount,
                Currency        = currency,
                LastSyncedAt    = DateTime.UtcNow,
            };
            await offerRepository.AddGameOfferAsync(newOffer, ct);

            logger.LogDebug(
                "Створено GameOffer: ExternalIdRecordId={Id}, Price={Price}",
                externalIdRecordId, newPrice);
            return;
        }

        // Тригер fn_sync_price_history (AFTER UPDATE) вирішує INSERT vs UPDATE.
        offer.CurrentPrice    = newPrice;
        offer.CurrentDiscount = newDiscount;
        offer.Currency        = currency;
        offer.LastSyncedAt    = DateTime.UtcNow;

        await offerRepository.UpdateGameOfferAsync(offer, ct);
    }
}
