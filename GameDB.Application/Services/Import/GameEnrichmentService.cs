using GameDB.Application.Interfaces;
using GameDB.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services.Import;

public sealed class GameEnrichmentService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IStoreProvider> providers,
    StoreGameMapper mapper,
    EnrichmentOperationState state,
    ILogger<GameEnrichmentService> logger) : IGameEnrichmentService
{
    private readonly IReadOnlyList<IStoreProvider> _providers = providers.ToList();

    public EnrichmentOperationState State => state;

    public async Task RunEnrichmentJobAsync(string? providerSlug, bool overwriteExisting, CancellationToken ct)
    {
        if (!state.TryStart())
        {
            logger.LogWarning("Спроба подвійного запуску збагачення ігор відхилена.");
            return;
        }

        state.OverwriteExisting = overwriteExisting;
        state.ResetProgress(0, providerSlug ?? "Всі магазини", "Збагачення деталей");

        try
        {
            var activeProviders = string.IsNullOrEmpty(providerSlug)
                ? _providers
                : _providers.Where(p => p.Slug.Equals(providerSlug, StringComparison.OrdinalIgnoreCase)).ToList();

            // Паралельно запускаємо збагачення для обраних провайдерів
            await Task.WhenAll(activeProviders.Select(p => ProcessProviderInternalAsync(p, ct)));
            
            state.MarkFinished("Збагачення завершено.");
        }
        catch (OperationCanceledException)
        {
            state.MarkFinished("Збагачення зупинено користувачем.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критична помилка під час збагачення");
            state.LastError = ex.Message;
            state.MarkFinished("Збагачення завершилося аварійно.");
        }
    }

    private async Task ProcessProviderInternalAsync(IStoreProvider provider, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        var externalIds = new List<string>();
        externalIds.AddRange(await repo.GetExternalIdsByStatusAsync(provider.ShopId, GameImportStatus.Basic, ct));
        externalIds.AddRange(await repo.GetExternalIdsByStatusAsync(provider.ShopId, GameImportStatus.Fail, ct));

        if(state.OverwriteExisting)
        {
            externalIds.AddRange(await repo.GetExternalIdsByStatusAsync(provider.ShopId, GameImportStatus.Full, ct));
        }
        if (externalIds.Count == 0) return;

        // Потокобезпечно додаємо кількість до глобального лічильника Total
        state.Total += externalIds.Count;

        // Розбиваємо на батчі для оптимізації запитів до API та БД
        const int batchSize = 50; 
        for (int i = 0; i < externalIds.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;

            var batch = externalIds.Skip(i).Take(batchSize).ToList();
            await EnrichBatchAsync(provider, batch, ct);
        }
    }

    private async Task EnrichBatchAsync(IStoreProvider provider, List<string> externalIds, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedRepo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        var games = await scopedRepo.GetGamesByExternalIdsBatchAsync(provider.ShopId, externalIds, ct);
        
        // Будуємо швидкий Dictionary для швидкого пошуку гри за її ExternalId у пам'яті
        var gamesDict = games
            .SelectMany(g => g.GameExternalIds
                .Where(e => e.ShopId == provider.ShopId && externalIds.Contains(e.ExternalId))
                .Select(e => new { e.ExternalId, Game = g }))
            .ToDictionary(x => x.ExternalId, x => x.Game);

        foreach (var externalId in externalIds)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;

            if (!gamesDict.TryGetValue(externalId, out var game))
            {
                // Якщо гри чомусь немає в базі, просто пропускаємо
                continue;
            }

            try
            {
                // Запитуємо деталі з API зовнішнього магазину
                var details = await provider.GetGameDetailsAsync(externalId, ct);
                if (details is null)
                {
                    game.ImportStatus = GameImportStatus.Fail;
                    await scopedRepo.UpdateAsync(game, ct);
                    state.IncrementFailed();
                }
                else
                {
                    // Мапимо отримані дані (жанри, теги, розробники тощо)
                    await mapper.ApplyAsync(game, details, scopedRepo, state.OverwriteExisting, ct);
                    game.ImportStatus = GameImportStatus.Full;
                    game.UpdatedAt = DateTime.UtcNow;

                    if (details.StoreUrl is not null)
                    {
                        var extId = game.GameExternalIds.FirstOrDefault(e => e.ShopId == provider.ShopId && e.ExternalId == externalId);
                        if (extId is not null) 
                            extId.ExternalUrl = details.StoreUrl;
                    }

                    // Зберігаємо оновлену сунтість у базу
                    await scopedRepo.UpdateAsync(game, ct);
                    state.IncrementProcessed();
                }
            }
            catch (Exception ex)
            {
                state.IncrementFailed();
                logger.LogError(ex, "[{Slug}] Помилка збагачення гри ExternalId: {Id}", provider.Slug, externalId);
            }

            // Робимо паузу між запитами, щоб API магазину не заблокувало наш воркер (Rate Limiting)
            await Task.Delay(provider.DelayBetweenRequestsMs, ct);
        }
    }
}