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
                    Console.WriteLine("Фоновий імпорт завершено. Всі ігри мають описи.");
                    _state.IsImportingDetails = false;
                    continue;
                }

                Console.WriteLine($"Запуск обробки батчу на {appIds.Count} ігор...");
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