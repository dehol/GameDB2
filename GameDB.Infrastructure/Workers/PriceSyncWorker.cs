using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using GameDB.Domain.Enums;
using GameDB.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Workers;

/// <summary>
/// Воркер фази 3: синхронізація цін.
/// Кожен провайдер обробляється паралельно (Task.WhenAll).
///
/// FIX прогрес-бугу: total передавався загальний (сума по всіх провайдерах),
/// але кожен SyncProviderAsync ітерував skip від 0 до total.
/// GetGamesBatchFromShopAsync фільтрує по shopId, тому батчі закінчувались
/// правильно (break при Empty), але progress-лічильник стану показував
/// некоректне "N/1500" — кожен провайдер рахував свій "шматок" від нуля
/// незалежно, а state.Total = 1500 = Steam(1000) + GOG(500).
///
/// Тепер: кожен провайдер отримує власний providerTotal,
/// ітерує від 0 до свого реального ліміту, state.Total = реальна сума.
/// </summary>
public sealed class PriceSyncWorker(
    IServiceProvider      serviceProvider,
    PriceSyncOperationState state,
    ILogger<PriceSyncWorker> logger)
    : MultiProviderBackgroundWorker<PriceSyncOperationState>(serviceProvider, state, logger)
{
    protected override string FinishedMessage  => "Синхронізацію цін завершено.";
    protected override string CancelledMessage => "Синхронізацію зупинено.";

    // Зберігаємо per-provider count між PrepareAsync і ProcessProviderAsync
    private readonly Dictionary<string, int> _providerCounts = new();

    protected override async Task PrepareAsync(List<IStoreProvider> providers, CancellationToken ct)
    {
        _providerCounts.Clear();

        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        foreach (var provider in providers)
        {
            var count = await repo.GetExternalIdsByStatusAsyncCount(
                provider.ShopId, GameImportStatus.Full, ct);
            _providerCounts[provider.Slug] = count;
        }

        // Реальний total = сума кожного провайдера
        State.ResetProgress(_providerCounts.Values.Sum(), "PriceSync", "Phase3");

        Logger.LogInformation("PriceSync: підготовлено {Total} ігор по {Count} провайдерах",
            State.Total, providers.Count);
    }

    protected override async Task ProcessProviderAsync(IStoreProvider provider, CancellationToken ct)
    {
        // Власний ліміт — не загальний total
        var providerTotal = _providerCounts.GetValueOrDefault(provider.Slug, 0);
        if (providerTotal == 0)
        {
            Logger.LogInformation("[{Slug}] Немає ігор для синхронізації цін", provider.Slug);
            return;
        }

        try
        {
            for (int skip = 0; skip < providerTotal && State.IsRunning && !ct.IsCancellationRequested; skip += 100)
            {
                List<Game> batch;

                using (var scope = ServiceProvider.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                    batch = await repo.GetGamesBatchFromShopAsync(skip, 100, provider.ShopId, ct);
                }

                if (batch.Count == 0) break;

                State.LastMessage = $"[{provider.Slug}] Ціни: {skip + 1}–{skip + batch.Count} з {providerTotal}";

                using (var scope = ServiceProvider.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<IPriceSyncService>();
                    await service.SyncPricesBatchAsync(provider, batch, State, ct);
                }
            }

            Logger.LogInformation("[{Slug}] Синхронізацію цін завершено", provider.Slug);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("[{Slug}] Синхронізацію цін зупинено", provider.Slug);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Slug}] Помилка синхронізації цін", provider.Slug);
            State.LastError = $"[{provider.Slug}] {ex.Message}";
        }
    }
}
