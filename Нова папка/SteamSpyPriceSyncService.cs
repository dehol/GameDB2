using System.Diagnostics;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public sealed class SteamSpyPriceSyncService(
    ISteamSpyClient steamSpy,
    IGameOfferRepository offerRepository,
    PriceManagerService priceManager,
    PriceSyncState priceState,
    ILogger<SteamSpyPriceSyncService> logger)
{
    private const int SteamShopId = 1;

    public async Task SyncPricesBatchAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default)
    {
        var validGames = games.Where(g => g.SteamAppId.HasValue).ToList();
        if (validGames.Count == 0)
        {
            logger.LogWarning("SteamSpy prices: no games with SteamAppId");
            return;
        }

        var metrics = new ImportBatchMetrics();
        var stopwatch = Stopwatch.StartNew();
        var gameIds = validGames.Select(g => g.GameId).ToList();
        var existingOffers = await offerRepository.GetOffersByGameIdsAsync(gameIds, SteamShopId, ct);
        var newOffers = new List<GameOffer>();

        logger.LogInformation("SteamSpy prices: syncing {Count} games", validGames.Count);

        foreach (var game in validGames)
        {
            if (ct.IsCancellationRequested) break;

            var appId = game.SteamAppId!.Value;
            try
            {
                var spy = await steamSpy.GetAppDetailsAsync(appId, ct);
                if (spy is null || !TryParsePrice(spy.InitialPrice ?? spy.Price, out var regularPrice))
                {
                    metrics.SkippedCount++;
                    continue;
                }

                short discount = 0;
                if (short.TryParse(spy.Discount, out var d))
                    discount = d;

                priceManager.StagePriceUpdate(
                    existingOffers,
                    newOffers,
                    gameId: game.GameId,
                    shopId: SteamShopId,
                    newPrice: regularPrice,
                    newDiscount: discount,
                    currency: "USD",
                    externalId: appId.ToString(),
                    downloadUrl: $"https://store.steampowered.com/app/{appId}/");

                metrics.SuccessCount++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                metrics.RateLimitCount++;
                metrics.ErrorCount++;
                logger.LogWarning(ex, "SteamSpy 429 price sync for AppId {AppId}", appId);
            }
            catch (Exception ex)
            {
                metrics.ErrorCount++;
                logger.LogError(ex, "SteamSpy price sync failed for AppId {AppId}", appId);
            }
        }

        if (newOffers.Count > 0)
            await offerRepository.AddRangeAsync(newOffers, ct);

        await offerRepository.SaveBatchAsync(ct);

        stopwatch.Stop();
        priceState.RecordBatch(metrics, stopwatch.Elapsed);
        logger.LogInformation(
            "SteamSpy price sync batch: {Summary} elapsed={Elapsed:mm\\:ss}",
            metrics.ToSummary(), stopwatch.Elapsed);
    }

    private static bool TryParsePrice(string? priceCents, out decimal price)
    {
        price = 0;
        if (string.IsNullOrWhiteSpace(priceCents)) return false;
        if (!int.TryParse(priceCents, out var cents)) return false;
        price = cents / 100m;
        return true;
    }
}
