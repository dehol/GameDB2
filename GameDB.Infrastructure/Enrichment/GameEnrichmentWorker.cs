using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using GameDB.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameDB.Infrastructure.Enrichment;

public sealed class GameEnrichmentWorker(
    IServiceProvider serviceProvider,
    EnrichmentOperationState state) : BackgroundService
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
                using var scope      = serviceProvider.CreateScope();
                var importService    = scope.ServiceProvider.GetRequiredService<StoreImportService>();
                var gamesRepo        = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var providers        = scope.ServiceProvider.GetRequiredService<IEnumerable<IStoreProvider>>();

                foreach (var provider in providers)
                {
                    if (!state.IsRunning) break;
                    state.CurrentProvider = provider.Slug;
                    state.CurrentPhase    = "enrich";

                    while (state.IsRunning && !stoppingToken.IsCancellationRequested)
                    {
                        var externalIds = state.OverwriteExisting
                            ? await gamesRepo.GetExternalIdsBatchAsync(
                                provider.ShopId, state.OverwriteSkip, 200, stoppingToken)
                            : await gamesRepo.GetExternalIdsByStatusAsync(
                                provider.ShopId, GameImportStatus.Basic, 200, stoppingToken);

                        if (externalIds.Count == 0)
                        {
                            state.LastMessage = $"[{provider.Slug}] Збагачення завершено.";
                            break;
                        }

                        state.BatchSize    = externalIds.Count;
                        state.LastMessage  = $"[{provider.Slug}] Збагачення: {externalIds.Count} ігор…";

                        await importService.EnrichBatchAsync(provider, externalIds, state, stoppingToken);

                        if (state.OverwriteExisting)
                            state.OverwriteSkip += externalIds.Count;
                    }
                }

                state.MarkFinished("Збагачення завершено для всіх магазинів.");
            }
            catch (Exception ex)
            {
                state.LastError = ex.Message;
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }
}
