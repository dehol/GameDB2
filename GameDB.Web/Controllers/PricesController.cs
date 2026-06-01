using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDB.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PricesController : ControllerBase
{
    private readonly IGameOfferRepository _offerRepository;
    private readonly IGameRepository _gameRepository;
    private readonly IAdminService _adminService;
    private readonly ILogger<PricesController> _logger;

    public PricesController(
        IGameOfferRepository offerRepository,
        IGameRepository gameRepository,
        IAdminService adminService,
        ILogger<PricesController> logger)
    {
        _offerRepository = offerRepository;
        _gameRepository = gameRepository;
        _adminService = adminService;
        _logger = logger;
    }

    [HttpGet("game/{gameId:int}")]
    public async Task<IActionResult> GetPricesByGameId(int gameId)
    {
        var gameExists = await _gameRepository.GetByIdAsync(gameId);
        if (gameExists == null)
            return NotFound(new { message = $"Гру з ID {gameId} не знайдено." });

        var offers = await _offerRepository.GetByGameIdAsync(gameId);
        
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

    [HttpPost("sync")]
    [Authorize(Roles = AuthCookieService.AdminRole)]
    public IActionResult TriggerPriceSync([FromQuery] int batchSize = 100)
    {
        if (batchSize <= 0 || batchSize > 200)
            return BadRequest(new { message = "Розмір батчу має бути від 1 до 200." });

        if (_adminService.StartPriceSync(batchSize))
        {
            return Accepted(new { message = "Синхронізацію успішно запущено у фоні." });
        }
        
        return BadRequest(new { message = "Синхронізація вже виконується." });
    }
}
