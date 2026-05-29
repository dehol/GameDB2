using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDB.Web.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = AuthCookieService.AdminRole)]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminController(IAdminService admin) => _admin = admin;

    [HttpGet("dashboard")]
    public Task<AdminDashboardDto> GetDashboard(CancellationToken ct)
        => _admin.GetDashboardAsync(ct);

    [HttpGet("games")]
    public Task<AdminGameListDto> GetGames(
        [FromQuery] AdminGameCoverageFilter filter = AdminGameCoverageFilter.All,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => _admin.GetGamesAsync(filter, page, pageSize, search, ct);

    [HttpPost("import/basic")]
    public async Task<IActionResult> ImportBasic(CancellationToken ct)
    {
        var imported = await _admin.ImportBasicGamesAsync(ct);
        return Ok(new { imported });
    }

    [HttpPost("import/details/start")]
    public IActionResult StartDetails([FromQuery] string source = "steam")
    {
        if (!string.Equals(source, "steam", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(source, "igdb", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "source має бути steam або igdb." });

        _admin.StartDetailsImport(source);
        return Accepted(new { message = $"Імпорт деталей ({source}) запущено." });
    }

    [HttpPost("import/details/stop")]
    public IActionResult StopDetails()
    {
        _admin.StopDetailsImport();
        return Ok(new { message = "Імпорт деталей зупинено." });
    }

    [HttpPost("import/prices/start")]
    public IActionResult StartPrices([FromQuery] int batchSize = 100)
    {
        if (!_admin.StartPriceSync(batchSize))
            return Conflict(new { message = "Синхронізація цін уже виконується." });

        return Accepted(new { message = "Синхронізацію цін запущено." });
    }

    [HttpPost("import/prices/stop")]
    public IActionResult StopPrices()
    {
        _admin.StopPriceSync();
        return Ok(new { message = "Зупинка синхронізації…" });
    }
}
