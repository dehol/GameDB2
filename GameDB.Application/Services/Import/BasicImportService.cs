using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Фаза 1 імпорту: завантажує список ігор з магазину і зберігає нові GameExternalId-зв'язки.
/// Зіставлення з існуючим каталогом відбувається по NormalizedName + точній назві.
/// </summary>
public sealed class BasicImportService(
    IServiceScopeFactory       scopeFactory,
    IOptions<StoreImportOptions> options,
    ILogger<BasicImportService> logger) : IBasicImportService
{
    private readonly StoreImportOptions _options = options.Value;

    public async Task<int> ImportBasicAsync(IStoreProvider provider, CancellationToken ct = default)
    {
        IReadOnlyCollection<StoreGameListItem> list = [];
        try
        {
            list = await provider.GetGameListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Slug}] Не вдалося отримати список ігор", provider.Slug);
            return 0;
        }

        var validItems = list.Where(i => provider.IsValidItem(i)).ToList();
        var candidates = validItems.Select(i => i.ExternalId).Distinct().ToList();

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
                logger.LogInformation("[{Slug}] Basic: {Done}/{Total}",
                    provider.Slug, imported, toProcess.Count);
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
    /// Один SaveChanges на весь батч.
    /// FIX: NormalizedName = normalized (не Slug!) — Slug — це частина URL магазину,
    /// а не нормалізована назва для cross-store матчингу.
    /// </summary>
    private static async Task ImportOrLinkBatchAsync(
        IGameRepository           repo,
        List<StoreGameListItem>   batch,
        IStoreProvider            provider,
        CancellationToken         ct)
    {
        var itemsWithNormalized = batch
            .Select(item => (Item: item, Normalized: GameNameNormalizer.Normalize(item.Name)))
            .Where(x => !string.IsNullOrEmpty(x.Normalized))
            .ToList();

        if (itemsWithNormalized.Count == 0) return;

        var names        = itemsWithNormalized.Select(x => x.Normalized).Distinct().ToList();
        var existingGames = await repo.GetGamesByNormalizedNamesAsync(names, ct);
        var existingDict  = existingGames
            .GroupBy(g => g.NormalizedName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var linkedInBatch = new HashSet<int>();
        var now           = DateTime.UtcNow;
        var newGames      = new List<Game>();
        var newLinks      = new List<GameExternalId>();

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
                    // FIX: завжди нормалізована назва, а не Slug магазину
                    NormalizedName = normalized,
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
}
