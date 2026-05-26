using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public sealed class ItadPriceSyncService(
    IItadClient         itadClient,
    PriceManagerService priceManager,
    ILogger<ItadPriceSyncService> logger)
{
    // Entry point: receives a full Game batch, splits free vs paid internally
    public async Task SyncPricesBatchAsync(
        IReadOnlyCollection<Game> games,
        CancellationToken ct = default)
    {

        if (games.Count == 0) return;

        // SteamId → DbGameId
        var steamIdToDbId = games.ToDictionary(g => g.SteamAppId!.Value, g => g.GameId);
        var steamIds      = steamIdToDbId.Keys.ToList();

        var uuidsMap = await itadClient.GetUuidsBySteamIdsAsync(steamIds, ct);
        if (uuidsMap.Count == 0)
        {
            logger.LogWarning("ITAD: no UUIDs found for {Count} paid games", games.Count);
            return;
        }

        // Inverted: UUID → SteamId(string) — O(1) lookup (ARCH-4 fix)
        var uuidToSteamId    = uuidsMap.ToDictionary(kv => kv.Value, kv => kv.Key);
        // SteamId(string) → DbGameId — O(1) lookup
        var steamIdStrToDbId = steamIdToDbId.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

        // GetPricesAsync must call /games/prices/v3 WITHOUT &deals=true
        // so it returns all stores including full-price (cut=0) entries
        var prices = await itadClient.GetPricesAsync(uuidsMap.Values.ToList(), ct);

        int updatedCount  = 0;
        int notFoundCount = 0;
        int noSteamCount  = 0;

        foreach (var priceData in prices)
        {
            if (!uuidToSteamId.TryGetValue(priceData.id, out var steamIdStr))    { notFoundCount++; continue; }
            if (!steamIdStrToDbId.TryGetValue(steamIdStr, out var dbGameId))      { notFoundCount++; continue; }

            // shop.id == 61 is Steam on ITAD
            // With deals=false, this entry exists for ALL games on Steam regardless of discount
            var steamOffer = priceData.deals.FirstOrDefault(d => d.shop.id == 61);

            if (steamOffer is null)
            {
                // Game is in ITAD but not listed on Steam store (removed, regional, etc.)
                noSteamCount++;
                continue;
            }

            // cut=0  → full price, amount == regular price
            // cut>0  → discounted, amount < regular price
            await priceManager.ProcessPriceUpdateAsync(
                gameId:      dbGameId,
                shopId:      1,
                newPrice:    steamOffer.price.amount,
                newDiscount: (short)steamOffer.cut,
                currency:    steamOffer.price.currency,
                externalId:  steamIdStr,
                downloadUrl: steamOffer.url,
                ct:          ct
            );
            updatedCount++;
        }

        logger.LogInformation(
            "ITAD: paid batch complete — updated {Updated}, no Steam offer {NoSteam}, UUID mismatch {NotFound}",
            updatedCount, noSteamCount, notFoundCount);
    }
}