using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services.Import;

public sealed class StoreImportService(
    IGameRepository games,
    StoreGameMapper mapper,
    PriceManagerService priceManager,
    IOptions<StoreImportOptions> options,
    ILogger<StoreImportService> logger)
{
    private readonly StoreImportOptions _options = options.Value;

    // ── Фаза 1: Basic import ────────────────────────────────────────────────

    public async Task<int> ImportBasicAsync(IStoreProvider provider, CancellationToken ct)
    {
        IReadOnlyCollection<StoreGameListItem> list = [];
        try { list = await provider.GetGameListAsync(ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Slug}] Не вдалося отримати список ігор", provider.Slug);
            return 0;
        }

        var candidates = list
            .Where(i => provider.IsValidItem(i))
            .Select(i => i.ExternalId)
            .Distinct()
            .ToList();

        var alreadyLinked = await games.GetExistingExternalIdsFromSetAsync(
            provider.ShopId, candidates, ct);

        var toProcess = list
            .Where(i => provider.IsValidItem(i) && !alreadyLinked.Contains(i.ExternalId))
            .ToList();

        int imported = 0;
        for (int i = 0; i < toProcess.Count; i += _options.BasicImportBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = toProcess.Skip(i).Take(_options.BasicImportBatchSize);
            foreach (var item in batch)
            {
                await ImportOrLinkAsync(item, provider, ct);
                imported++;
            }
            logger.LogInformation("[{Slug}] Basic: {Done}/{Total}",
                provider.Slug, imported, toProcess.Count);
        }
        return imported;
    }

    // ── Фаза 2: Enrich ─────────────────────────────────────────────────────

    public async Task EnrichBatchAsync(
        IStoreProvider provider,
        List<string> externalIds,
        EnrichmentOperationState state,
        CancellationToken ct)
    {
        foreach (var externalId in externalIds)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;
            try
            {
                await EnrichSingleAsync(provider, externalId, state.OverwriteExisting, ct);
                state.Processed++;
                await Task.Delay(provider.DelayBetweenRequestsMs, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Slug}] Помилка збагачення {Id}", provider.Slug, externalId);
            }
        }
    }

    // ── Фаза 3: Price sync ─────────────────────────────────────────────────

    public async Task SyncPricesBatchAsync(
        IStoreProvider provider,
        IReadOnlyCollection<Game> gameBatch,
        PriceSyncOperationState state,
        CancellationToken ct)
    {
        var pairs = gameBatch
            .SelectMany(g => g.ExternalIds
                .Where(e => e.ShopId == provider.ShopId)
                .Select(e => (Game: g, ExternalId: e.ExternalId)))
            .ToList();

        if (pairs.Count == 0) return;

        foreach (var (game, externalId) in pairs)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;
            try
            {
                var price = await provider.GetPriceAsync(externalId, ct);
                if (price is null) continue;

                await priceManager.ProcessPriceUpdateAsync(
                    gameId:      game.GameId,
                    shopId:      provider.ShopId,
                    newPrice:    price.Price,
                    newDiscount: price.Discount,
                    currency:    price.Currency,
                    externalId:  externalId,
                    downloadUrl: price.StoreUrl,
                    ct:          ct);

                state.Processed++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Slug}] Помилка ціни {Id}", provider.Slug, externalId);
            }
            await Task.Delay(provider.DelayBetweenRequestsMs, ct);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task ImportOrLinkAsync(
        StoreGameListItem item, IStoreProvider provider, CancellationToken ct)
    {
        var normalizedName = GameNameNormalizer.Normalize(item.Name);
        var existing = await games.FindByNormalizedNameAsync(normalizedName, ct);

        if (existing is not null)
        {
            bool alreadyLinked = existing.ExternalIds.Any(e => e.ShopId == provider.ShopId);
            if (alreadyLinked) return;

            await games.AddExternalIdAsync(new GameExternalId
            {
                GameId     = existing.GameId,
                ShopId     = provider.ShopId,
                ExternalId = item.ExternalId,
                CreatedAt  = DateTime.UtcNow
            }, ct);
            return;
        }

        var now = DateTime.UtcNow;
        var game = new Game
        {
            Name           = item.Name,
            NormalizedName = normalizedName,
            ImportStatus   = GameImportStatus.Basic,
            CreatedAt      = now,
            UpdatedAt      = now
        };
        game.ExternalIds.Add(new GameExternalId
        {
            ShopId     = provider.ShopId,
            ExternalId = item.ExternalId,
            CreatedAt  = now
        });
        await games.AddAsync(game, ct);
    }

    private async Task EnrichSingleAsync(
        IStoreProvider provider, string externalId, bool overwrite, CancellationToken ct)
    {
        var game = await games.GetByExternalIdAsync(provider.ShopId, externalId, ct);
        if (game is null) return;

        var details = await provider.GetGameDetailsAsync(externalId, ct);
        if (details is null)
        {
            game.ImportStatus = GameImportStatus.Fail;
            await games.UpdateAsync(game, ct);
            return;
        }

        await mapper.ApplyAsync(game, details, games, overwrite, ct);
        game.ImportStatus = GameImportStatus.Full;
        game.UpdatedAt    = DateTime.UtcNow;
        await games.UpdateAsync(game, ct);

        logger.LogInformation("[{Slug}] Збагачено: {Name}", provider.Slug, game.Name);
    }
}
