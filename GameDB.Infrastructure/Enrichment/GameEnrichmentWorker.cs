using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GameDB.Infrastructure.Enrichment;

public sealed class GameEnrichmentWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GameEnrichmentImportState _state;

    public GameEnrichmentWorker(IServiceProvider serviceProvider, GameEnrichmentImportState state)
    {
        _serviceProvider = serviceProvider;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_state.IsImporting)
            {
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var gamesRepo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var enrichment = scope.ServiceProvider.GetRequiredService<GameEnrichmentService>();

                while (_state.IsImporting && !stoppingToken.IsCancellationRequested)
                {
                    var appIds = _state.OverwriteExisting
                        ? await gamesRepo.GetSteamAppIdsBatchAsync(_state.OverwriteSkip, 200, stoppingToken)
                        : await gamesRepo.GetAppIdsWithoutDetailsAsync(200, stoppingToken);
                    if (appIds.Count == 0)
                    {
                        _state.IsImporting = false;
                        _state.FinishedAt = DateTime.UtcNow;
                        _state.LastMessage = _state.OverwriteExisting
                            ? "Збагачення завершено."
                            : "Усі ігри мають описи.";
                        break;
                    }

                    _state.LastBatchSize = appIds.Count;
                    _state.LastMessage = _state.OverwriteExisting
                        ? $"Збагачення: {_state.OverwriteSkip + 1}–{_state.OverwriteSkip + appIds.Count}…"
                        : $"Збагачення: {appIds.Count} ігор…";
                    await enrichment.ImportBatchAsync(appIds, _state, stoppingToken);
                    if (_state.OverwriteExisting)
                        _state.OverwriteSkip += appIds.Count;
                }
            }
            catch (Exception ex)
            {
                _state.LastError = ex.Message;
                Console.WriteLine($"GameEnrichment worker error: {ex.Message}");
                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}
