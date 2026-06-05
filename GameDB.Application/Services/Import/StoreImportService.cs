using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services.Import;

public sealed class StoreImportService(
    IServiceScopeFactory scopeFactory,
    StoreGameMapper mapper,
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

        var validItems = list.Where(i => provider.IsValidItem(i)).ToList();
        var candidates = validItems.Select(i => i.ExternalId).Distinct().ToList();

        // Один запит: перевіряємо лише candidates, а не весь каталог магазину
        HashSet<string> alreadyLinked;
        using (var scope = scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            alreadyLinked = await repo.GetExistingExternalIdsFromSetAsync(provider.ShopId, candidates, ct);
        }

        var toProcess = validItems.Where(i => !alreadyLinked.Contains(i.ExternalId)).ToList();

        if (toProcess.Count == 0)
        {
            logger.LogInformation("[{Slug}] Basic: нічого нового", provider.Slug);
            return 0;
        }

        int imported = 0;
        for (int i = 0; i < toProcess.Count; i += _options.BasicImportBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = toProcess.Skip(i).Take(_options.BasicImportBatchSize).ToList();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                await ImportOrLinkBatchAsync(repo, batch, provider, ct);
                imported += batch.Count;
                logger.LogInformation("[{Slug}] Basic: {Done}/{Total}", provider.Slug, imported, toProcess.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Slug}] Basic: помилка батчу {From}-{To}",
                    provider.Slug, i + 1, i + batch.Count);
            }
        }
        return imported;
    }

    /// <summary>
    /// FIX: Один SaveChanges на весь батч (ImportBatchAsync) замість N окремих.
    /// FIX: ExternalUrl заповнюється одразу при Basic-імпорті.
    /// FIX: Відстежує вже пов'язані GameId в межах батчу → не дублює посилання.
    /// </summary>
    private async Task ImportOrLinkBatchAsync(
        IGameRepository repo,
        List<StoreGameListItem> batch,
        IStoreProvider provider,
        CancellationToken ct)
    {
        var itemsWithNormalized = batch
            .Select(item => (Item: item, Normalized: GameNameNormalizer.Normalize(item.Name)))
            .Where(x => !string.IsNullOrEmpty(x.Normalized))
            .ToList();

        if (itemsWithNormalized.Count == 0) return;

        var names = itemsWithNormalized.Select(x => x.Normalized).Distinct().ToList();
        var existingGames = await repo.GetGamesByNormalizedNamesAsync(names, ct);
        var existingDict = existingGames
            .GroupBy(g => g.NormalizedName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var linkedInBatch = new HashSet<int>();
        var now = DateTime.UtcNow;
        var newGames = new List<Game>();
        var newLinks = new List<GameExternalId>();

        foreach (var (item, normalized) in itemsWithNormalized)
        {
            Game? existing = null;
            if (existingDict.TryGetValue(normalized, out var candidateGames))
                existing = candidateGames.FirstOrDefault(g =>
                    g.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (existing.GameExternalIds.Any(e => e.ShopId == provider.ShopId)
                    || linkedInBatch.Contains(existing.GameId))
                    continue;

                newLinks.Add(new GameExternalId
                {
                    GameId      = existing.GameId,
                    ShopId      = provider.ShopId,
                    ExternalId  = item.ExternalId,
                    ExternalUrl = provider.BuildOfferUrl(item.Slug ?? item.ExternalId),
                    CreatedAt   = now
                });
                linkedInBatch.Add(existing.GameId);
            }
            else
            {
                var game = new Game
                {
                    Name           = item.Name,
                    NormalizedName = item.Slug ?? normalized,
                    ImportStatus   = GameImportStatus.Basic,
                    CreatedAt      = now,
                    UpdatedAt      = now
                };
                game.GameExternalIds.Add(new GameExternalId
                {
                    ShopId      = provider.ShopId,
                    ExternalId  = item.ExternalId,
                    ExternalUrl = provider.BuildOfferUrl(item.Slug ?? item.ExternalId),
                    CreatedAt   = now
                });
                newGames.Add(game);

                if (!existingDict.TryGetValue(normalized, out var lst))
                    existingDict[normalized] = lst = [];
                lst.Add(game);
            }
        }

        if (newGames.Count > 0 || newLinks.Count > 0)
            await repo.ImportBatchAsync(newGames, newLinks, ct);
    }

    // ── Фаза 2: Enrichment ──────────────────────────────────────────────────

    public async Task EnrichBatchAsync(
        IStoreProvider provider,
        List<string> externalIds,
        EnrichmentOperationState state,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        foreach (var externalId in externalIds)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;
            try
            {
                await EnrichSingleAsync(repo, provider, externalId, state.OverwriteExisting, ct);
                state.IncrementProcessed();
                await Task.Delay(provider.DelayBetweenRequestsMs, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Slug}] Помилка збагачення {Id}", provider.Slug, externalId);
            }
        }
    }

    private async Task EnrichSingleAsync(
        IGameRepository repo,
        IStoreProvider provider,
        string externalId,
        bool overwrite,
        CancellationToken ct)
    {
        var game = await repo.GetByExternalIdAsync(provider.ShopId, externalId, ct);
        if (game is null) return;

        var details = await provider.GetGameDetailsAsync(externalId, ct);
        if (details is null)
        {
            game.ImportStatus = GameImportStatus.Fail;
            await repo.UpdateAsync(game, ct);
            return;
        }

        await mapper.ApplyAsync(game, details, repo, overwrite, ct);
        game.ImportStatus = GameImportStatus.Full;
        game.UpdatedAt    = DateTime.UtcNow;

        if (details.StoreUrl is not null)
        {
            var extId = game.GameExternalIds
                .FirstOrDefault(e => e.ShopId == provider.ShopId && e.ExternalId == externalId);
            if (extId is not null)
                extId.ExternalUrl = details.StoreUrl;
        }

        await repo.UpdateAsync(game, ct);
        logger.LogInformation("[{Slug}] Збагачено: {Name}", provider.Slug, game.Name);
    }

    // ── Фаза 3: Price sync ─────────────────────────────────────────────────

    public async Task SyncPricesBatchAsync(
        IStoreProvider provider,
        IReadOnlyCollection<Game> gameBatch,
        PriceSyncOperationState state,
        CancellationToken ct)
    {
        // FIX: зберігаємо весь запис GameExternalId (а не тільки рядковий ExternalId),
        // щоб мати доступ до e.Id (integer FK) для ProcessPriceUpdateAsync.
        var pairs = gameBatch
            .SelectMany(g => g.GameExternalIds
                .Where(e => e.ShopId == provider.ShopId)
                .Select(e => (Game: g, ExternalIdRecord: e)))
            .ToList();

        if (pairs.Count == 0) return;

        using var scope = scopeFactory.CreateScope();
        var priceManager = scope.ServiceProvider.GetRequiredService<PriceManagerService>();

        foreach (var (game, externalIdRecord) in pairs)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;
            try
            {
                // Рядковий ExternalId ("730", "cyberpunk-2077" тощо) — для виклику API магазину.
                var price = await provider.GetPriceAsync(externalIdRecord.ExternalId, ct);
                if (price is null) continue;

                // Integer FK (GameExternalId.Id) — для запису/оновлення GameOffer у БД.
                await priceManager.ProcessPriceUpdateAsync(
                    externalIdRecordId: externalIdRecord.Id,
                    newPrice:           price.Price,
                    newDiscount:        price.Discount,
                    currency:           price.Currency,
                    ct:                 ct);

                state.IncrementProcessed();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Slug}] Помилка ціни {Id}", provider.Slug, externalIdRecord.ExternalId);
            }
            await Task.Delay(provider.DelayBetweenRequestsMs, ct);
        }
    }
}