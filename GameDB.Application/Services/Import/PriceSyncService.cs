using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services.Import;

public sealed class PriceSyncService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IStoreProvider> providers,
    IPriceManagerService priceManager,
    PriceSyncOperationState state,
    ILogger<PriceSyncService> logger) : IPriceSyncService
{
    private readonly IReadOnlyList<IStoreProvider> _providers = providers.ToList();

    public PriceSyncOperationState State => state;

    public async Task RunPriceSyncJobAsync(string? providerSlug, CancellationToken ct)
    {
        if (!state.TryStart())
        {
            logger.LogWarning("Спроба подвійного запуску синхронізації цін відхилена.");
            return;
        }

        try
        {
            var activeProviders = string.IsNullOrEmpty(providerSlug)
                ? _providers
                : _providers.Where(p => p.Slug.Equals(providerSlug, StringComparison.OrdinalIgnoreCase)).ToList();

            int totalGamesCount = 0;
            foreach (var p in activeProviders)
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                totalGamesCount = await repo.GetTotalGamesCountAsync(ct);
            }

            state.ResetProgress(totalGamesCount, providerSlug ?? "Всі магазини", "Синхронізація цін");

            foreach (var provider in activeProviders)
            {
                if (ct.IsCancellationRequested || !state.IsRunning) break;
                await SyncPricesForProviderAsync(provider, ct);
            }

            state.MarkFinished("Синхронізацію цін завершено.");
        }
        catch (OperationCanceledException)
        {
            state.MarkFinished("Синхронізацію цін зупинено користувачем.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критична помилка під час синхронізації цін");
            state.LastError = ex.Message;
            state.MarkFinished("Синхронізація цін завершилася аварійно.");
        }
    }

    private async Task SyncPricesForProviderAsync(IStoreProvider provider, CancellationToken ct)
    {
        const int batchSize = 100;
        int skip = 0;

        while (state.IsRunning && !ct.IsCancellationRequested)
        {
            List<Game> gameBatch;
            using (var scope = scopeFactory.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                gameBatch = await repo.GetGamesBatchFromShopAsync(skip, batchSize, provider.ShopId, ct);
            }

            if (gameBatch.Count == 0) break;

            await SyncPricesBatchInternalAsync(provider, gameBatch, ct);
            skip += gameBatch.Count;
        }
    }

    private async Task SyncPricesBatchInternalAsync(IStoreProvider provider, IReadOnlyCollection<Game> gameBatch, CancellationToken ct)
    {
        var pairs = gameBatch
            .SelectMany(g => g.GameExternalIds
                .Where(e => e.ShopId == provider.ShopId)
                .Select(e => (Game: g, ExternalIdRecord: e)))
            .ToList();

        if (pairs.Count == 0) return;

        foreach (var (game, externalIdRecord) in pairs)
        {
            if (ct.IsCancellationRequested || !state.IsRunning) break;

            try
            {
                var price = await provider.GetPriceAsync(externalIdRecord.ExternalId, ct);
                if (price is null) continue;

                await priceManager.ProcessPriceUpdateAsync(
                    externalIdRecordId: externalIdRecord.Id,
                    newPrice: price.Price,
                    newDiscount: price.Discount,
                    currency: price.Currency,
                    ct: ct);

                state.IncrementProcessed();
            }
            catch (Exception ex)
            {
                state.IncrementFailed();
                logger.LogError(ex, "[{Slug}] Не вдалося оновити ціну для ExternalId: {Id}", provider.Slug, externalIdRecord.ExternalId);
            }

            await Task.Delay(provider.DelayBetweenRequestsMs, ct);
        }
    }
}