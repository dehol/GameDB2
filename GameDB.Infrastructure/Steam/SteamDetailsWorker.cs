using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using GameDB.Application.Interfaces;
using GameDB.Application.Services;

namespace GameDB.Infrastructure.Steam;

public class SteamDetailsWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SteamImportState _state;

    public SteamDetailsWorker(IServiceProvider serviceProvider, SteamImportState state)
    {
        _serviceProvider = serviceProvider;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_state.IsImportingDetails)
            {
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var gamesRepo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                var syncService = scope.ServiceProvider.GetRequiredService<SteamImportService>();

                // Отримуємо наступний батч
                var appIds = await gamesRepo.GetAppIdsWithoutDetailsAsync(200);

                if (appIds.Count == 0)
                {
                    _state.IsImportingDetails = false;
                    _state.FinishedAt = DateTime.UtcNow;
                    _state.LastMessage = "Усі ігри мають описи.";
                    continue;
                }

                _state.LastBatchSize = appIds.Count;
                _state.LastMessage = $"Обробка батчу: {appIds.Count} ігор…";
                await syncService.ImportDetailsBatchAsync(appIds, _state, stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка у фоновому воркері: {ex.Message}");
                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}