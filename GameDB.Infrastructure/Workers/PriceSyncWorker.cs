using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameDB.Infrastructure.Workers;

public sealed class PriceSyncWorker(
    IServiceProvider serviceProvider,
    PriceSyncOperationState state) : BackgroundService
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

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, state.Cts?.Token ?? CancellationToken.None);
            var ct = linked.Token;

            try
            {
                using var scope       = serviceProvider.CreateScope();
                var importService     = scope.ServiceProvider.GetRequiredService<StoreImportService>();
                var gamesRepo         = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var providers         = scope.ServiceProvider.GetRequiredService<IEnumerable<IStoreProvider>>();

                foreach (var provider in providers)
                {
                    if (!state.IsRunning || ct.IsCancellationRequested) break;
                    state.CurrentProvider = provider.Slug;
                    state.CurrentPhase    = "prices";

                    var total = await gamesRepo.GetTotalGamesCountAsync(ct);
                    state.ResetProgress(total, provider.Slug, "prices");

                    for (int skip = 0; skip < total && state.IsRunning && !ct.IsCancellationRequested; skip += 100)
                    {
                        var batch = await gamesRepo.GetGamesBatchAsync(skip, 100, ct);
                        state.BatchSize   = batch.Count;
                        state.LastMessage = $"[{provider.Slug}] Ціни: {skip + 1}–{skip + batch.Count} з {total}";

                        await importService.SyncPricesBatchAsync(provider, batch, state, ct);
                    }
                }

                state.MarkFinished("Синхронізацію цін завершено.");
            }
            catch (OperationCanceledException)
            {
                state.MarkFinished("Синхронізацію зупинено.");
            }
            catch (Exception ex)
            {
                state.LastError = ex.Message;
                state.MarkFinished("Помилка синхронізації.");
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }
}
