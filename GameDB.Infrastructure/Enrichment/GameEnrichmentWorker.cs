using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Domain.Enums;
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
                using var scope    = _serviceProvider.CreateScope();
                var gamesRepo      = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var enrichment     = scope.ServiceProvider.GetRequiredService<GameEnrichmentService>();

                while (_state.IsImporting && !stoppingToken.IsCancellationRequested)
                {
                    List<string> externalIds;

                    if (_state.OverwriteExisting)
                    {
                        externalIds = await gamesRepo.GetExternalIdsBatchAsync(
                            SteamSpyImportService.SteamShopId, _state.OverwriteSkip, 200, stoppingToken);
                    }
                    else
                    {
                        externalIds = await gamesRepo.GetExternalIdsByStatusAsync(
                            SteamSpyImportService.SteamShopId, GameImportStatus.Basic, 200, stoppingToken);
                    }

                    if (externalIds.Count == 0)
                    {
                        _state.IsImporting = false;
                        _state.FinishedAt  = DateTime.UtcNow;
                        _state.LastMessage = _state.OverwriteExisting
                            ? "Збагачення завершено."
                            : "Усі ігри вже збагачені.";
                        break;
                    }

                    _state.LastBatchSize = externalIds.Count;
                    _state.LastMessage   = _state.OverwriteExisting
                        ? $"Збагачення: {_state.OverwriteSkip + 1}–{_state.OverwriteSkip + externalIds.Count}…"
                        : $"Збагачення: {externalIds.Count} ігор…";

                    await enrichment.ImportBatchAsync(externalIds, _state, stoppingToken);

                    if (_state.OverwriteExisting)
                        _state.OverwriteSkip += externalIds.Count;
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