using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using GameDB.Domain.Enums;
using GameDB.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Workers;

/// <summary>
/// Воркер фази 2: збагачення ігор деталями.
/// Кожен провайдер обробляється паралельно (Task.WhenAll).
/// Кожен провайдер незалежно веде свій offset при OverwriteExisting.
/// </summary>
public sealed class GameEnrichmentWorker(
    IServiceProvider         serviceProvider,
    ImportOperationState state,
    ILogger<GameEnrichmentWorker> logger)
    : MultiProviderBackgroundWorker<ImportOperationState>(serviceProvider, state, logger)
{
    protected override string FinishedMessage  => "Збагачення завершено для всіх магазинів.";
    protected override string CancelledMessage => "Збагачення зупинено.";

    protected override Task PrepareAsync(List<IStoreProvider> providers, CancellationToken ct)
        => Task.CompletedTask; // Enrichment не потребує попереднього підрахунку

    protected override async Task ProcessProviderAsync(IStoreProvider provider, CancellationToken ct)
    {
        // Власний offset — кожен провайдер незалежний (різні ShopId)
        int overwriteSkip = 0;

        try
        {
            while (State.IsRunning && !ct.IsCancellationRequested)
            {
                List<string> externalIds;

                using (var scope = ServiceProvider.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                    externalIds = State.OverwriteExisting
                        ? await repo.GetExternalIdsBatchAsync(
                            provider.ShopId, overwriteSkip, 200, ct)
                        : await repo.GetExternalIdsByStatusAsync(
                            provider.ShopId, GameImportStatus.Basic, 200, ct);
                }

                if (externalIds.Count == 0)
                {
                    Logger.LogInformation("[{Slug}] Збагачення завершено", provider.Slug);
                    break;
                }

                State.LastMessage = $"[{provider.Slug}] Збагачення: {externalIds.Count} ігор…";

                // Новий scope → новий DbContext на кожен батч
                using (var scope = ServiceProvider.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<IGameEnrichmentService>();
                    await service.EnrichBatchAsync(provider, externalIds, State, ct);
                }

                if (State.OverwriteExisting)
                    overwriteSkip += externalIds.Count;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("[{Slug}] Збагачення зупинено", provider.Slug);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Slug}] Помилка збагачення провайдера", provider.Slug);
            State.LastError = $"[{provider.Slug}] {ex.Message}";
        }
    }
}
