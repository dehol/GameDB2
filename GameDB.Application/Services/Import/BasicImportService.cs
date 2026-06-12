using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services.Import;

public sealed class BasicImportService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IStoreProvider> providers,
    IOptions<StoreImportOptions> options,
    BasicImportOperationState state,
    ILogger<BasicImportService> logger) : IBasicImportService
{
    private readonly StoreImportOptions        _options   = options.Value;
    private readonly IReadOnlyList<IStoreProvider> _providers = providers.ToList();

    public BasicImportOperationState State => state;

    /// <remarks>
    /// Зупинка через кнопку адмін-панелі спрацьовує між батчами:
    /// RequestStop() → IsRunning = false — перевіряється в циклах нижче.
    /// StopToken не лінкується до ct, тому поточний await не переривається,
    /// на відміну від GameEnrichmentService де StopToken ввімкнений у LinkedCts.
    /// </remarks>
    public async Task RunImportJobAsync(string? providerSlug, CancellationToken ct)
    {
        if (!state.TryStart())
        {
            logger.LogWarning("Спроба подвійного запуску базового імпорту відхилена.");
            return;
        }

        try
        {
            var targetProviders = string.IsNullOrWhiteSpace(providerSlug)
                ? _providers
                : _providers.Where(p => string.Equals(p.Slug, providerSlug, StringComparison.OrdinalIgnoreCase)).ToList();

            state.ResetProgress(0, providerSlug ?? "Всі магазини", "Базовий імпорт");

            foreach (var provider in targetProviders)
            {
                if (ct.IsCancellationRequested || !state.IsRunning) break;

                logger.LogInformation("[{Slug}] Запуск базового імпорту", provider.Slug);
                state.LastMessage = $"[{provider.Slug}] Завантаження списку ігор з API...";

                await ImportBasicInternalAsync(provider, ct);
            }

            state.MarkFinished("Базовий імпорт успішно завершено.");
        }
        catch (OperationCanceledException)
        {
            state.MarkFinished("Базовий імпорт зупинено користувачем.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критична помилка базового імпорту");
            state.LastError = ex.Message;
            state.MarkFinished("Базовий імпорт завершився аварійно.");
        }
    }

    private async Task ImportBasicInternalAsync(IStoreProvider provider, CancellationToken ct)
    {
        IReadOnlyCollection<StoreGameListItem> list = [];
        try
        {
            list = await provider.GetGameListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Slug}] Не вдалося отримати список ігор", provider.Slug);
            state.LastError = $"[{provider.Slug}] Помилка API: {ex.Message}";
            return;
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
            state.LastMessage = $"[{provider.Slug}] Немає нових ігор для імпорту.";
            return;
        }

        state.AddToTotal(toProcess.Count);

        int imported = 0;
        for (int i = 0; i < toProcess.Count; i += _options.BasicImportBatchSize)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;

            var batch = toProcess.Skip(i).Take(_options.BasicImportBatchSize).ToList();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

                await ImportOrLinkBatchAsync(repo, batch, provider, ct);

                imported += batch.Count;
                state.AddToProcessed(batch.Count);
                state.LastMessage = $"[{provider.Slug}] Імпортовано {imported} з {toProcess.Count} нових ігор";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Slug}] Basic: помилка батчу", provider.Slug);
                state.IncrementFailed();
                state.LastError = $"[{provider.Slug}] Помилка батчу: {ex.Message}";
            }
        }
    }

    private static async Task ImportOrLinkBatchAsync(
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

        var names         = itemsWithNormalized.Select(x => x.Normalized).Distinct().ToList();
        var existingGames = await repo.GetGamesByNormalizedNamesAsync(names, ct);
        var existingDict  = existingGames
            .GroupBy(g => g.NormalizedName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var linkedInBatch = new HashSet<int>();
        var now      = DateTime.UtcNow;
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
                    GameId     = existing.GameId,
                    ShopId     = provider.ShopId,
                    ExternalId = item.ExternalId,
                    ExternalUrl = provider.BuildOfferUrl(item.Slug ?? item.ExternalId),
                    CreatedAt  = now
                });
                linkedInBatch.Add(existing.GameId);
            }
            else
            {
                var game = new Game
                {
                    Name           = item.Name,
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
