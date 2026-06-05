using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

/// <summary>
/// Оновлює GameOffer при синхронізації ціни.
/// PriceHistory керується автоматично тригером fn_sync_price_history:
///   - ціна змінилась → тригер INSERT новий PriceHistory з RecordedAt = NOW()
///   - ціна та сама  → тригер UPDATE LastSyncedAt = NOW() на поточному рядку
///
/// GameOffer тепер пов'язаний з GameExternalId.Id (integer FK) замість GameId + ShopId.
/// Щоб отримати GameOffer для конкретної гри: Game → ExternalIds[i] → ExternalIds[i].GameOffer.
/// </summary>
public sealed class PriceManagerService(
    IGameOfferRepository offerRepository,
    ILogger<PriceManagerService> logger)
{
    /// <summary>
    /// Оновлює або створює GameOffer для вказаного запису GameExternalId.
    /// </summary>
    /// <param name="externalIdRecordId">
    /// GameExternalId.Id — цілочисельний PK запису, що зв'язує гру з магазином.
    /// Отримується з <c>game.ExternalIds.First(e => e.ShopId == shopId).Id</c>.
    /// </param>
    public async Task ProcessPriceUpdateAsync(
        int      externalIdRecordId,
        decimal  newPrice,
        short    newDiscount,
        string   currency,
        CancellationToken ct = default)
    {
        var offer = await offerRepository.GetByExternalIdRecordAsync(externalIdRecordId, ct);

        if (offer is null)
        {
            // Перший раз бачимо цей оффер — створюємо.
            // Тригер trg_sync_price_history (AFTER INSERT) вставить перший PriceHistory автоматично.
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
                "Створено новий GameOffer: ExternalIdRecordId={ExternalIdRecordId}, Price={Price}",
                externalIdRecordId, newPrice);

            return;
        }

        // Оновлюємо GameOffer.
        // Тригер fn_sync_price_history на AFTER UPDATE OF CurrentPrice, CurrentDiscount:
        //   - якщо ціна змінилась → INSERT в PriceHistory (новий RecordedAt)
        //   - якщо ціна та сама  → UPDATE LastSyncedAt in-place
        // LowestPrice / LowestPriceDate підтримує тригер fn_recalculate_lowest_price.
        offer.CurrentPrice    = newPrice;
        offer.CurrentDiscount = newDiscount;
        offer.Currency        = currency;
        offer.LastSyncedAt    = DateTime.UtcNow;

        await offerRepository.UpdateGameOfferAsync(offer, ct);
    }
}