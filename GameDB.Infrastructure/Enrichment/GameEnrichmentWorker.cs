using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using GameDB.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Enrichment;

/// <summary>
/// FIX: Провайдери запускаються паралельно через Task.WhenAll.
/// Кожен провайдер — окремий DbContext scope → немає конфліктів.
/// Кожен провайдер незалежно відстежує власний overwrite offset.
/// Помилка одного не зупиняє решту.
/// </summary>
public sealed class GameEnrichmentWorker(
    IServiceProvider serviceProvider,
    EnrichmentOperationState state,
    ILogger<GameEnrichmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!state.IsRunning)
            {
                await Task.Delay(5_000, stoppingToken);
                continue;
            }

            try
            {
                List<IStoreProvider> providers;
                using (var scope = serviceProvider.CreateScope())
                    providers = scope.ServiceProvider
                        .GetRequiredService<IEnumerable<IStoreProvider>>().ToList();

                await Task.WhenAll(providers.Select(p => EnrichProviderAsync(p, stoppingToken)));

                state.MarkFinished("Збагачення завершено для всіх магазинів.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GameEnrichmentWorker: непередбачена помилка");
                state.LastError = ex.Message;
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    private async Task EnrichProviderAsync(IStoreProvider provider, CancellationToken stoppingToken)
    {
        // Власний offset — кожен провайдер незалежний (різні ShopId, різні набори ігор)
        int overwriteSkip = 0;

        try
        {
            while (state.IsRunning && !stoppingToken.IsCancellationRequested)
            {
                using var scope   = serviceProvider.CreateScope();
                var repo          = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var importService = scope.ServiceProvider.GetRequiredService<StoreImportService>();

                var externalIds = state.OverwriteExisting
                    ? await repo.GetExternalIdsBatchAsync(provider.ShopId, overwriteSkip, 200, stoppingToken)
                    : await repo.GetExternalIdsByStatusAsync(provider.ShopId, GameImportStatus.Basic, 200, stoppingToken);

                if (externalIds.Count == 0)
                {
                    logger.LogInformation("[{Slug}] Збагачення завершено", provider.Slug);
                    break;
                }

                state.LastMessage = $"[{provider.Slug}] Збагачення: {externalIds.Count} ігор…";
                await importService.EnrichBatchAsync(provider, externalIds, state, stoppingToken);

                if (state.OverwriteExisting)
                    overwriteSkip += externalIds.Count;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Slug}] Помилка збагачення провайдера", provider.Slug);
            state.LastError = $"[{provider.Slug}] {ex.Message}";
        }
    }
}
