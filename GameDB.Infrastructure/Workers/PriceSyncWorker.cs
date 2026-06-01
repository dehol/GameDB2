using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Workers;

/// <summary>
/// FIX: Провайдери запускаються паралельно через Task.WhenAll.
/// Кожен провайдер — окремий DbContext scope на кожен батч.
/// Помилка одного не зупиняє решту.
/// </summary>
public sealed class PriceSyncWorker(
    IServiceProvider serviceProvider,
    PriceSyncOperationState state,
    ILogger<PriceSyncWorker> logger) : BackgroundService
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
                List<IStoreProvider> providers;
                using (var scope = serviceProvider.CreateScope())
                    providers = scope.ServiceProvider
                        .GetRequiredService<IEnumerable<IStoreProvider>>().ToList();

                int total;
                using (var scope = serviceProvider.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                    total = await repo.GetTotalGamesCountAsync(ct);
                }

                // Прогрес: обидва провайдери обробляють однаковий набір ігор
                state.Processed = 0;
                state.Total     = total * providers.Count;

                await Task.WhenAll(providers.Select(p => SyncProviderAsync(p, total, ct)));

                state.MarkFinished("Синхронізацію цін завершено.");
            }
            catch (OperationCanceledException)
            {
                state.MarkFinished("Синхронізацію зупинено.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PriceSyncWorker: непередбачена помилка");
                state.LastError = ex.Message;
                state.MarkFinished("Помилка синхронізації.");
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    private async Task SyncProviderAsync(IStoreProvider provider, int total, CancellationToken ct)
    {
        try
        {
            for (int skip = 0; skip < total && state.IsRunning && !ct.IsCancellationRequested; skip += 100)
            {
                using var scope   = serviceProvider.CreateScope();
                var repo          = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var importService = scope.ServiceProvider.GetRequiredService<StoreImportService>();

                var batch = await repo.GetGamesBatchAsync(skip, 100, ct);
                if (batch.Count == 0) break;

                state.LastMessage = $"[{provider.Slug}] Ціни: {skip + 1}–{skip + batch.Count} з {total}";
                await importService.SyncPricesBatchAsync(provider, batch, state, ct);
            }
            logger.LogInformation("[{Slug}] Синхронізацію цін завершено", provider.Slug);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[{Slug}] Синхронізацію цін зупинено", provider.Slug);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Slug}] Помилка синхронізації цін", provider.Slug);
            state.LastError = $"[{provider.Slug}] {ex.Message}";
        }
    }
}
