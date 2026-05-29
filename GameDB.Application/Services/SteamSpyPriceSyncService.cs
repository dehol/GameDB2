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
        var validGames = games.Where(g => g.SteamAppId.HasValue).ToList();
        if (validGames.Count == 0)
        {
            logger.LogWarning("SteamSpy prices: no games with SteamAppId");
            return;
        }

        logger.LogInformation("SteamSpy prices: syncing {Count} games", validGames.Count);

        int updated = 0;
        int skipped = 0;

        foreach (var game in validGames)
        {
            if (ct.IsCancellationRequested) break;

            var appId = game.SteamAppId!.Value;
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
                    gameId: game.GameId,
                    shopId: 1,
                    newPrice: regularPrice,
                    newDiscount: discount,
                    currency: "USD",
                    externalId: appId.ToString(),
                    downloadUrl: $"https://store.steampowered.com/app/{appId}/",
                    ct: ct);

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
            updated, skipped, validGames.Count);
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
