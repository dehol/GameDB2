using System.Collections.Concurrent;
using System.Diagnostics;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public sealed class SteamSpyPriceSyncService(
    ISteamSpyClient steamSpy,
    IGameOfferRepository offerRepository,
    PriceManagerService priceManager,
    PriceSyncState priceState,
    ILogger<SteamSpyPriceSyncService> logger,
    IOptions<SteamSpyImportOptions> options)
{
    private const int SteamShopId = 1;
    private readonly SteamSpyImportOptions _options = options.Value;

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
        var concurrency = Math.Max(1, _options.PriceSyncConcurrency);
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);

        logger.LogInformation("SteamSpy prices: syncing {Count} games", validGames.Count);

        var tasks = validGames.Select(game => SyncSingleGameAsync(
            game, existingOffers, newOffers, metrics, semaphore, ct));

        await Task.WhenAll(tasks);

        if (newOffers.Count > 0)
            await offerRepository.AddRangeAsync(newOffers, ct);

        await offerRepository.SaveBatchAsync(ct);

        stopwatch.Stop();
        priceState.RecordBatch(metrics, stopwatch.Elapsed);
        logger.LogInformation(
            "SteamSpy price sync batch: {Summary} elapsed={Elapsed:mm\\:ss}",
            metrics.ToSummary(), stopwatch.Elapsed);
    }

    private async Task SyncSingleGameAsync(
        Game game,
        Dictionary<int, GameOffer> existingOffers,
        List<GameOffer> newOffers,
        ImportBatchMetrics metrics,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        var appId = game.SteamAppId!.Value;
        await semaphore.WaitAsync(ct);
        try
        {
            var spy = await steamSpy.GetAppDetailsAsync(appId, ct);
            if (spy is null || !TryParsePrice(spy.InitialPrice ?? spy.Price, out var regularPrice))
            {
                lock (metrics) { metrics.SkippedCount++; }
                return;
            }

            short discount = 0;
            if (short.TryParse(spy.Discount, out var d))
                discount = d;

            lock (newOffers)
            {
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
            }

            lock (metrics) { metrics.SuccessCount++; }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            lock (metrics)
            {
                metrics.RateLimitCount++;
                metrics.ErrorCount++;
            }
            logger.LogWarning(ex, "SteamSpy 429 price sync for AppId {AppId}", appId);
        }
        catch (Exception ex)
        {
            lock (metrics) { metrics.ErrorCount++; }
            logger.LogError(ex, "SteamSpy price sync failed for AppId {AppId}", appId);
        }
        finally
        {
            semaphore.Release();
        }
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
