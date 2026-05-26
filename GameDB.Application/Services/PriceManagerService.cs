using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

/// <summary>
/// Оновлює GameOffer при синхронізації ціни.
/// PriceHistory керується автоматично тригером fn_sync_price_history:
///   - ціна змінилась → тригер INSERT новий PriceHistory з RecordedAt = NOW()
///   - ціна та сама  → тригер UPDATE LastSyncedAt = NOW() на поточному рядку
/// C# більше не створює PriceHistory вручну.
/// </summary>
public sealed class PriceManagerService(
    IGameOfferRepository offerRepository,
    ILogger<PriceManagerService> logger)
{
    public async Task ProcessPriceUpdateAsync(
        int gameId,
        int shopId,
        decimal newPrice,
        short newDiscount,
        string currency,
        string? externalId = null,
        string? downloadUrl = null,
        CancellationToken ct = default)
    {
        var offer = await offerRepository.GetGameOfferAsync(gameId, shopId, ct);

        if (offer is null)
        {
            // Перший раз бачимо цей оффер — створюємо.
            // Тригер trg_sync_price_history (AFTER INSERT на GameOffer через PriceHistories)
            // або AFTER UPDATE вставить перший PriceHistory автоматично.
            var newOffer = new GameOffer
            {
                GameId          = gameId,
                ShopId          = shopId,
                ExternalId      = externalId,
                DownloadUrl     = downloadUrl,
                CurrentPrice    = newPrice,
                CurrentDiscount = newDiscount,
                Currency        = currency,
                LastSyncedAt    = DateTime.UtcNow
            };

            await offerRepository.AddGameOfferAsync(newOffer, ct);

            logger.LogDebug(
                "Створено новий GameOffer: GameId={GameId}, ShopId={ShopId}, Price={Price}",
                gameId, shopId, newPrice);

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

        if (downloadUrl is not null)
            offer.DownloadUrl = downloadUrl;

        if (externalId is not null)
            offer.ExternalId = externalId;

        await offerRepository.UpdateGameOfferAsync(offer, ct);
    }
}