using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Workers;

/// <summary>
/// Абстрактна база для воркерів, що паралельно обробляють дані декількох магазинів.
///
/// Спільний патерн між GameEnrichmentWorker і PriceSyncWorker:
///   - Idle loop з 5-секундним polling
///   - LinkedCancellationTokenSource (host stop + manual CTS)
///   - Task.WhenAll для паралельних провайдерів
///   - Обробка OperationCanceledException / Exception
///
/// Підклас реалізує:
///   - PrepareAsync  — підрахунок total, ініціалізація стану
///   - ProcessProviderAsync — логіка для одного провайдера
/// </summary>
public abstract class MultiProviderBackgroundWorker<TState>(
    IServiceProvider serviceProvider,
    ImportOperationState           state,
    ILogger          logger) : BackgroundService
{
    protected IServiceProvider ServiceProvider => serviceProvider;
    protected ImportOperationState            State           => state;
    protected ILogger           Logger          => logger;

    protected abstract string FinishedMessage  { get; }
    protected abstract string CancelledMessage { get; }

    /// <summary>Викликається перед Task.WhenAll — для підрахунку total та інших підготовчих кроків.</summary>
    protected abstract Task PrepareAsync(List<IStoreProvider> providers, CancellationToken ct);

    /// <summary>Обробка одного провайдера — виконується в паралельному Task.</summary>
    protected abstract Task ProcessProviderAsync(IStoreProvider provider, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!state.IsRunning)
            {
                await Task.Delay(5_000, stoppingToken);
                continue;
            }

            // Дозволяє зупинити конкретний воркер через state.Cts без зупинки хоста
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken, state.Cts?.Token ?? CancellationToken.None);
            var ct = linked.Token;

            try
            {
                var providers = ResolveProviders();
                await PrepareAsync(providers, ct);
                await Task.WhenAll(providers.Select(p => ProcessProviderAsync(p, ct)));
                state.MarkFinished(FinishedMessage);
            }
            catch (OperationCanceledException)
            {
                state.MarkFinished(CancelledMessage);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Worker}: непередбачена помилка", GetType().Name);
                state.LastError = ex.Message;
                state.MarkFinished($"{GetType().Name}: помилка.");
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    /// <summary>Ізолює резолюцію провайдерів у власний scope (не забруднює тривалий scope).</summary>
    protected List<IStoreProvider> ResolveProviders()
    {
        using var scope = serviceProvider.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<IEnumerable<IStoreProvider>>()
            .ToList();
    }
}
