using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GameDB.Domain.Entities;

namespace GameDB.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PricesController : ControllerBase
{
    private readonly IGameOfferRepository _offerRepository;
    private readonly IGameRepository _gameRepository;
    private readonly ItadPriceSyncService _itadSyncService;
    private readonly ILogger<PricesController> _logger;

    public PricesController(
        IGameOfferRepository offerRepository,
        IGameRepository gameRepository,
        ItadPriceSyncService itadSyncService,
        ILogger<PricesController> logger)
    {
        _offerRepository = offerRepository;
        _gameRepository = gameRepository;
        _itadSyncService = itadSyncService;
        _logger = logger;
    }

    /// <summary>
    /// Отримати всі пропозиції та ціни для конкретної гри (для сторінки гри в каталозі)
    /// </summary>
    [HttpGet("game/{gameId:int}")]
    public async Task<IActionResult> GetPricesByGameId(int gameId)
    {
        var gameExists = await _gameRepository.GetByIdAsync(gameId);
        if (gameExists == null)
        {
            return NotFound(new { message = $"Гру з ID {gameId} не знайдено в базі." });
        }

        var offers = await _offerRepository.GetByGameIdAsync(gameId);
        
        // Мапимо у чистий DTO для фронтенду, щоб не світити системні ID історії цін
        var result = offers.Select(o => new
        {
            o.GameOfferId,
            ShopName = o.Shop?.Name ?? "Unknown Shop",
            o.CurrentPrice,
            o.CurrentDiscount,
            o.Currency,
            o.DownloadUrl,
            o.LastSyncedAt
        });

        return Ok(result);
    }

    /// <summary>
    /// Ручний запуск повної синхронізації цін у фоновому режимі з детальними логами
    /// </summary>
    [HttpPost("sync")]
    [Authorize(Roles = AuthCookieService.AdminRole)]
    public IActionResult TriggerPriceSync([FromQuery] int batchSize = 100)
    {
        if (batchSize <= 0 || batchSize > 200)
        {
            return BadRequest(new { message = "Розмір батчу має бути в межах від 1 до 200 (ліміт ITAD API)." });
        }

        _logger.LogInformation("🔄 Запущено ручну синхронізацію цін. Передаємо процес у фоновий потік...");

        // Запускаємо важку задачу в окремому потоці (Task.Run), 
        // щоб миттєво віддати відповідь браузеру
        Task.Run(async () =>
        {
            // Створюємо новий Scope для доступу до бази даних у фоні
            using var scope = HttpContext.RequestServices.CreateScope();
            var gameRepo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var itadSync = scope.ServiceProvider.GetRequiredService<ItadPriceSyncService>();
            var bgLogger = scope.ServiceProvider.GetRequiredService<ILogger<PricesController>>();

            try
            {
                int totalGames = await gameRepo.GetTotalGamesCountAsync();
                bgLogger.LogInformation("🚀 [SYNC] Знайдено {Total} ігор у базі. Починаємо масовий імпорт цін...", totalGames);
                
                for (int skip = 0; skip < totalGames; skip += batchSize)
                {
                    // ЛОГУЄМО ПРОГРЕС ЗАГАЛЬНОГО ЦИКЛУ
                    bgLogger.LogInformation("📦 [SYNC] Беремо батч ігор: {Start} - {End} із {Total}", 
                        skip, Math.Min(skip + batchSize, totalGames), totalGames);

                    var batch = await gameRepo.GetGamesBatchAsync(skip, batchSize);

                    if (batch.Any())
                    {
                        await itadSync.SyncPricesBatchAsync(batch);
                    }
                    else
                    {
                        bgLogger.LogInformation("⏭️ [SYNC] У цьому батчі немає ігор зі SteamAppId. Пропускаємо.");
                    }
                }
                
                bgLogger.LogInformation("🎉 [SYNC] ФОНОВА СИНХРОНІЗАЦІЯ ВСІХ ІГОР УСПІШНО ЗАВЕРШЕНА!");
            }
            catch (Exception ex)
            {
                bgLogger.LogError(ex, "❌ Помилка фонової синхронізації цін.");
            }
        });

        // Миттєво повертаємо статус 202 Accepted (Запит прийнято в обробку)
        return Accepted(new { message = "Синхронізацію успішно запущено у фоні! Відкрий консоль сервера, щоб бачити прогрес." });
    }
}