using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

/// <summary>
/// Оновлює GameOffer при синхронізації ціни.
/// PriceHistory керується автоматично тригером fn_sync_price_history.
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
            var newOffer = CreateOffer(gameId, shopId, newPrice, newDiscount, currency, externalId, downloadUrl);
            await offerRepository.AddGameOfferAsync(newOffer, ct);
            logger.LogDebug("Створено новий GameOffer: GameId={GameId}, ShopId={ShopId}, Price={Price}",
                gameId, shopId, newPrice);
            return;
        }

        ApplyPriceUpdate(offer, newPrice, newDiscount, currency, externalId, downloadUrl);
        await offerRepository.UpdateGameOfferAsync(offer, ct);
    }

    public void StagePriceUpdate(
        Dictionary<int, GameOffer> existingOffers,
        List<GameOffer> newOffers,
        int gameId,
        int shopId,
        decimal newPrice,
        short newDiscount,
        string currency,
        string? externalId = null,
        string? downloadUrl = null)
    {
        existingOffers.TryGetValue(gameId, out var offer);

        if (offer is null)
        {
            var created = CreateOffer(gameId, shopId, newPrice, newDiscount, currency, externalId, downloadUrl);
            newOffers.Add(created);
            existingOffers[gameId] = created;
            return;
        }

        ApplyPriceUpdate(offer, newPrice, newDiscount, currency, externalId, downloadUrl);
    }

    private static GameOffer CreateOffer(
        int gameId,
        int shopId,
        decimal newPrice,
        short newDiscount,
        string currency,
        string? externalId,
        string? downloadUrl)
        => new()
        {
            GameId = gameId,
            ShopId = shopId,
            ExternalId = externalId,
            DownloadUrl = downloadUrl,
            CurrentPrice = newPrice,
            CurrentDiscount = newDiscount,
            Currency = currency,
            LastSyncedAt = DateTime.UtcNow
        };

    private static void ApplyPriceUpdate(
        GameOffer offer,
        decimal newPrice,
        short newDiscount,
        string currency,
        string? externalId,
        string? downloadUrl)
    {
        offer.CurrentPrice = newPrice;
        offer.CurrentDiscount = newDiscount;
        offer.Currency = currency;
        offer.LastSyncedAt = DateTime.UtcNow;

        if (downloadUrl is not null)
            offer.DownloadUrl = downloadUrl;

        if (externalId is not null)
            offer.ExternalId = externalId;
    }
}
