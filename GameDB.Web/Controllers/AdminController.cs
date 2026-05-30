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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportBasic(CancellationToken ct)
    {
        var imported = await _admin.ImportBasicGamesAsync(ct);
        return Ok(new { imported });
    }

    [HttpPost("import/enrich/start")]
    [ValidateAntiForgeryToken]
    public IActionResult StartEnrichment([FromQuery] bool overwrite = false)
    {
        _admin.StartEnrichmentImport(overwrite);
        return Accepted(new { message = "Збагачення (SteamSpy) запущено." });
    }

    [HttpPost("import/enrich/stop")]
    [ValidateAntiForgeryToken]
    public IActionResult StopEnrichment()
    {
        _admin.StopEnrichmentImport();
        return Ok(new { message = "Збагачення зупинено." });
    }

    [HttpPost("import/details/start")]
    [ValidateAntiForgeryToken]
    public IActionResult StartDetailsLegacy([FromQuery] bool overwrite = false)
    {
        _admin.StartEnrichmentImport(overwrite);
        return Accepted(new { message = "Збагачення (SteamSpy) запущено." });
    }

    [HttpPost("import/details/stop")]
    [ValidateAntiForgeryToken]
    public IActionResult StopDetails()
    {
        _admin.StopEnrichmentImport();
        return Ok(new { message = "Збагачення зупинено." });
    }

    [HttpPost("import/prices/start")]
    [ValidateAntiForgeryToken]
    public IActionResult StartPrices([FromQuery] int batchSize = 100)
    {
        if (!_admin.StartPriceSync(batchSize))
            return Conflict(new { message = "Синхронізація цін уже виконується." });

        return Accepted(new { message = "Синхронізацію цін (SteamSpy) запущено." });
    }

    [HttpPost("import/prices/stop")]
    [ValidateAntiForgeryToken]
    public IActionResult StopPrices()
    {
        _admin.StopPriceSync();
        return Ok(new { message = "Зупинка синхронізації…" });
    }
}
