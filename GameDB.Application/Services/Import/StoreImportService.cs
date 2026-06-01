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

        var candidates = list
            .Where(i => provider.IsValidItem(i))
            .Select(i => i.ExternalId)
            .Distinct()
            .ToList();

        List<string> alreadyLinked;
        using (var scope = scopeFactory.CreateScope())
        {
            var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            alreadyLinked = [.. await games.GetExistingExternalIdsFromSetAsync(provider.ShopId, candidates, ct)];
        }

        var toProcess = list
            .Where(i => provider.IsValidItem(i) && !alreadyLinked.Contains(i.ExternalId))
            .ToList();

        int imported = 0;
        for (int i = 0; i < toProcess.Count; i += _options.BasicImportBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = toProcess.Skip(i).Take(_options.BasicImportBatchSize).ToList();

            using (var scope = scopeFactory.CreateScope())
            {
                var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                await ImportOrLinkBatchAsync(games, batch, provider, ct);
                imported += batch.Count;
            }

            logger.LogInformation("[{Slug}] Basic: {Done}/{Total}",
                provider.Slug, imported, toProcess.Count);
        }
        return imported;
    }

    private async Task ImportOrLinkBatchAsync(
    IGameRepository gamesRepo, 
    List<StoreGameListItem> batch, 
    IStoreProvider provider, 
    CancellationToken ct)
{
    var itemsWithNormalized = batch
        .Select(item => (Item: item, Normalized: GameNameNormalizer.Normalize(item.Name)))
        .Where(x => !string.IsNullOrEmpty(x.Normalized))
        .ToList();

    var distinctNormalizedNames = itemsWithNormalized
        .Select(x => x.Normalized)
        .Distinct()
        .ToList();

    // 1. Отримуємо ігри з бази
    var existingGames = await gamesRepo.GetGamesByNormalizedNamesAsync(distinctNormalizedNames, ct);
    
    var existingGamesDict = existingGames
        .GroupBy(g => g.NormalizedName)
        .ToDictionary(g => g.Key, g => g.ToList());

    // КРИТИЧНИЙ ФІКС: Хеш-сет для відстеження ігор, які ми ВЖЕ пов'язали з цим магазином У ЦІЙ ПАЧЦІ
    var linkedGameIdsInBatch = new HashSet<int>();

    var now = DateTime.UtcNow;

    foreach (var pair in itemsWithNormalized)
    {
        Game? existing = null;

        if (existingGamesDict.TryGetValue(pair.Normalized, out var candidates))
        {
            existing = candidates.FirstOrDefault(g => g.Name.Equals(pair.Item.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (existing is not null)
        {
            // КРИТИЧНИЙ ФІКС: Перевіряємо строго наявність магазину (БЕЗ ExternalId),
            // а також дивимось, чи не додавали ми йому лінк у цьому ж циклі мілісекунду назад
            bool linkExists = existing.ExternalIds.Any(e => e.ShopId == provider.ShopId) 
                              || linkedGameIdsInBatch.Contains(existing.GameId);
            
            if (linkExists) continue;

            await gamesRepo.AddExternalIdAsync(new GameExternalId
            {
                GameId = existing.GameId,
                ShopId = provider.ShopId,
                ExternalId = pair.Item.ExternalId,
                CreatedAt = now
            }, ct);

            // Запам'ятовуємо, що для цієї гри в межах пачки лінк вже створено
            linkedGameIdsInBatch.Add(existing.GameId);
        }
        else
        {
            var game = new Game
            {
                Name = pair.Item.Name,
                NormalizedName = pair.Normalized,
                ImportStatus = GameImportStatus.Basic,
                CreatedAt = now,
                UpdatedAt = now
            };
            game.ExternalIds.Add(new GameExternalId
            {
                ShopId = provider.ShopId,
                ExternalId = pair.Item.ExternalId,
                CreatedAt = now
            });

            await gamesRepo.AddAsync(game, ct);
            
            if (!existingGamesDict.TryGetValue(pair.Normalized, out var list))
            {
                list = [];
                existingGamesDict[pair.Normalized] = list;
            }
            list.Add(game);
        }
    }
}

    public async Task EnrichBatchAsync(
        IStoreProvider provider,
        List<string> externalIds,
        EnrichmentOperationState state,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        foreach (var externalId in externalIds)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;
            try
            {
                await EnrichSingleAsync(games, provider, externalId, state.OverwriteExisting, ct);
                state.Processed++;
                await Task.Delay(provider.DelayBetweenRequestsMs, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Slug}] Помилка збагачення {Id}", provider.Slug, externalId);
            }
        }
    }

    private async Task EnrichSingleAsync(
        IGameRepository gamesRepo,
        IStoreProvider provider, 
        string externalId, 
        bool overwrite, 
        CancellationToken ct)
    {
        var game = await gamesRepo.GetByExternalIdAsync(provider.ShopId, externalId, ct);
        if (game is null) return;

        var details = await provider.GetGameDetailsAsync(externalId, ct);
        if (details is null)
        {
            game.ImportStatus = GameImportStatus.Fail;
            await gamesRepo.UpdateAsync(game, ct);
            return;
        }

        await mapper.ApplyAsync(game, details, gamesRepo, overwrite, ct);
        game.ImportStatus = GameImportStatus.Full;
        game.UpdatedAt = DateTime.UtcNow;
        await gamesRepo.UpdateAsync(game, ct);

        logger.LogInformation("[{Slug}] Збагачено: {Name}", provider.Slug, game.Name);
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

        using var scope = scopeFactory.CreateScope();
        var priceManager = scope.ServiceProvider.GetRequiredService<PriceManagerService>();

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
}