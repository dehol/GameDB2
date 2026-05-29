using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public sealed class ItadPriceSyncService(
    IItadClient itadClient,
    PriceManagerService priceManager,
    ILogger<ItadPriceSyncService> logger)
{
    // Entry point: receives a full Game batch, splits free vs paid internally
    public async Task SyncPricesBatchAsync(
        IReadOnlyCollection<Game> games,
        CancellationToken ct = default)
    {
        // 1. Фільтруємо тільки ігри зі SteamAppId
        var validGames = games
            .Where(g => g.SteamAppId.HasValue)
            .ToList();

        if (validGames.Count == 0)
        {
            logger.LogWarning("ITAD: no valid games with SteamAppId");
            return;
        }

        logger.LogInformation(
            "ITAD: starting sync for {Count} games",
            validGames.Count);

        var steamIdToDbId = validGames.ToDictionary(
            g => g.SteamAppId!.Value,
            g => g.GameId);

        var steamIds = steamIdToDbId.Keys.ToList();

        // 2. UUID mapping
        var uuidsMap = await itadClient.GetUuidsBySteamIdsAsync(steamIds, ct);

        if (uuidsMap.Count == 0)
        {
            logger.LogWarning(
                "ITAD: no UUIDs found for {Count} games",
                validGames.Count);

            return;
        }

        logger.LogInformation(
            "ITAD: resolved {Resolved}/{Total} UUIDs",
            uuidsMap.Count,
            validGames.Count);

        // Лог ігор без UUID
        var missingUuidGames = steamIds
            .Where(id => !uuidsMap.ContainsKey(id.ToString()))
            .ToList();

        if (missingUuidGames.Count > 0)
        {
            logger.LogWarning(
                "ITAD: games without UUIDs: {Games}",
                string.Join(", ", missingUuidGames));
        }

        var uuidToSteamId = uuidsMap.ToDictionary(
            kv => kv.Value,
            kv => kv.Key);

        var steamIdStrToDbId = steamIdToDbId.ToDictionary(
            kv => kv.Key.ToString(),
            kv => kv.Value);

        // 3. Prices request
        var prices = await itadClient.GetPricesAsync(
            uuidsMap.Values.ToList(),
            ct);

        logger.LogInformation(
            "ITAD: received {Count} price responses",
            prices.Count);

        int updatedCount = 0;
        int notFoundCount = 0;
        int noSteamCount = 0;
        int nullPriceDataCount = 0;

        foreach (var priceData in prices)
        {
            // NULL check
            if (priceData?.id == null)
            {
                nullPriceDataCount++;

                logger.LogWarning(
                    "ITAD: received null/invalid priceData object");

                continue;
            }

            // UUID -> SteamId
            if (!uuidToSteamId.TryGetValue(priceData.id, out var steamIdStr))
            {
                notFoundCount++;

                logger.LogWarning(
                    "ITAD: UUID {Uuid} not found in local mapping",
                    priceData.id);

                continue;
            }

            // SteamId -> DbGameId
            if (!steamIdStrToDbId.TryGetValue(steamIdStr, out var dbGameId))
            {
                notFoundCount++;

                logger.LogWarning(
                    "ITAD: SteamId {SteamId} not found in DB mapping",
                    steamIdStr);

                continue;
            }

            // Детальний лог deals
            if (priceData.deals == null || priceData.deals.Count == 0)
            {
                logger.LogWarning(
                    "ITAD: no deals returned for Steam app {SteamId}",
                    steamIdStr);

                noSteamCount++;
                continue;
            }

            logger.LogDebug(
                "ITAD: app {SteamId} has {DealsCount} deals",
                steamIdStr,
                priceData.deals.Count);

            foreach (var deal in priceData.deals)
            {
                logger.LogDebug(
                    """
                    ITAD Deal:
                    SteamAppId: {SteamId}
                    ShopId: {ShopId}
                    ShopName: {ShopName}
                    HasPrice: {HasPrice}
                    PriceAmount: {Price}
                    Currency: {Currency}
                    Url: {Url}
                    """,
                    steamIdStr,
                    deal?.shop?.id,
                    deal?.shop?.name,
                    deal?.price != null,
                    deal?.price?.amount,
                    deal?.price?.currency,
                    deal?.url);
            }

            // Пошук Steam deal
            var steamOffer = priceData.deals
                .FirstOrDefault(d => d?.shop?.id == 61);

            if (steamOffer == null)
            {
                logger.LogWarning(
                    """
                    ITAD: no Steam shop offer found for app {SteamId}

                    Available shops:
                    {Shops}
                    """,
                    steamIdStr,
                    string.Join(
                        ", ",
                        priceData.deals.Select(d =>
                            $"{d?.shop?.id}:{d?.shop?.name}")));

                noSteamCount++;
                continue;
            }

            if (steamOffer.price == null)
            {
                logger.LogWarning(
                    """
                    ITAD: Steam offer exists but price is NULL for app {SteamId}

                    ShopId: {ShopId}
                    ShopName: {ShopName}
                    Url: {Url}
                    """,
                    steamIdStr,
                    steamOffer.shop?.id,
                    steamOffer.shop?.name,
                    steamOffer.url);

                noSteamCount++;
                continue;
            }

            try
            {
                await priceManager.ProcessPriceUpdateAsync(
                    gameId: dbGameId,
                    shopId: 1,
                    newPrice: steamOffer.regular.amount,
                    newDiscount: (short)steamOffer.cut,
                    currency: steamOffer.price.currency ?? "USD",
                    externalId: steamIdStr,
                    downloadUrl: steamOffer.url,
                    ct: ct
                );

                updatedCount++;

                logger.LogInformation(
                    """
                    ITAD: updated game {SteamId}

                    Price: {Price}
                    Discount: {Discount}
                    Currency: {Currency}
                    """,
                    steamIdStr,
                    steamOffer.regular.amount,
                    steamOffer.cut,
                    steamOffer.price.currency);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "ITAD: failed updating price for Steam app {SteamId}",
                    steamIdStr);
            }
        }

        logger.LogInformation(
            """
            ITAD sync complete

            Updated: {Updated}
            NoSteamOffer: {NoSteam}
            MappingErrors: {NotFound}
            NullPriceData: {NullPriceData}
            TotalInput: {Total}
            """,
            updatedCount,
            noSteamCount,
            notFoundCount,
            nullPriceDataCount,
            validGames.Count);
    }
}