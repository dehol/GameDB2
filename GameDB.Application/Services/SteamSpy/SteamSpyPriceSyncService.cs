using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public sealed class SteamSpyPriceSyncService(
    ISteamSpyClient steamSpy,
    PriceManagerService priceManager,
    ILogger<SteamSpyPriceSyncService> logger,
    IOptions<SteamSpyImportOptions> options)
{
    private readonly SteamSpyImportOptions _options = options.Value;

    public async Task SyncPricesBatchAsync(IReadOnlyCollection<Game> games, CancellationToken ct = default)
    {
        // Steam AppId — рядок у GameExternalId, парсимо тут у Steam-сервісі
        var validPairs = games
            .SelectMany(g => g.ExternalIds
                .Where(e => e.ShopId == SteamSpyImportService.SteamShopId
                            && int.TryParse(e.ExternalId, out _))
                .Select(e => (Game: g, AppId: int.Parse(e.ExternalId))))
            .ToList();

        if (validPairs.Count == 0)
        {
            logger.LogWarning("SteamSpy prices: no games with Steam ExternalId");
            return;
        }

        logger.LogInformation("SteamSpy prices: syncing {Count} games", validPairs.Count);

        int updated = 0, skipped = 0;

        foreach (var (game, appId) in validPairs)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var spy = await steamSpy.GetAppDetailsAsync(appId, ct);
                if (spy is null || !TryParsePrice(spy.InitialPrice ?? spy.Price, out var regularPrice))
                {
                    skipped++;
                    continue;
                }

                short discount = 0;
                if (short.TryParse(spy.Discount, out var d))
                    discount = d;

                await priceManager.ProcessPriceUpdateAsync(
                    gameId:      game.GameId,
                    shopId:      SteamSpyImportService.SteamShopId,
                    newPrice:    regularPrice,
                    newDiscount: discount,
                    currency:    "USD",
                    externalId:  appId.ToString(),
                    downloadUrl: SteamSpyImportService.BuildStoreUrl(appId),
                    ct:          ct);

                updated++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SteamSpy price sync failed for AppId {AppId}", appId);
            }

            await Task.Delay(_options.DelayBetweenRequestsMs, ct);
        }

        logger.LogInformation(
            "SteamSpy price sync complete: updated={Updated}, skipped={Skipped}, total={Total}",
            updated, skipped, validPairs.Count);
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