using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using GameDB.Application.Interfaces;
using GameDB.Application.Services;

namespace GameDB.Infrastructure.Igdb;

public class IgdbDetailsWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IgdbImportState _state;

    public IgdbDetailsWorker(IServiceProvider serviceProvider, IgdbImportState state)
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
                var igdbService = scope.ServiceProvider.GetRequiredService<FastIgdbImportService>();

                // Process batches until none left or import stopped
                while (_state.IsImporting && !stoppingToken.IsCancellationRequested)
                {
                    var appIds = await gamesRepo.GetAppIdsWithoutDetailsAsync(200);
                    if (appIds.Count == 0)
                    {
                        _state.IsImporting = false;
                        break;
                    }

                    await igdbService.ImportDetailsFastAsync(appIds, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"IGDB worker error: {ex.Message}");
                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}
