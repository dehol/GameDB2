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
    public Task<AdminDashboardDto> GetDashboard()
        => _admin.GetDashboardAsync();

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
    public async Task<IActionResult> ImportBasic([FromQuery] string? provider, CancellationToken ct)
    {
        try
        {
            var imported = await _admin.ImportBasicGamesAsync(provider, ct);
            return Ok(new { imported });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("import/enrich/start")]
    [ValidateAntiForgeryToken]
    public IActionResult StartEnrichment([FromQuery] string? provider, [FromQuery] bool overwrite = false)
    {
        _admin.StartEnrichmentImport(provider, overwrite);
        return Accepted(new { message = "Збагачення запущено." });
    }

    [HttpPost("import/enrich/stop")]
    [ValidateAntiForgeryToken]
    public IActionResult StopEnrichment()
    {
        _admin.StopEnrichmentImport();
        return Ok(new { message = "Збагачення зупинено." });
    }

    [HttpPost("import/prices/start")]
    [ValidateAntiForgeryToken]
    public IActionResult StartPrices([FromQuery] string? provider, [FromQuery] int batchSize = 100, [FromQuery] DateTime? notSyncedSince = null)
    {
        if (!_admin.StartPriceSync(batchSize, provider, notSyncedSince))
            return Conflict(new { message = "Синхронізація цін уже виконується." });

        var msg = notSyncedSince.HasValue
            ? $"Синхронізацію цін (не синхронізовані після {notSyncedSince.Value:dd.MM.yyyy}) запущено."
            : "Синхронізацію цін запущено.";

        return Accepted(new { message = msg });
    }

    [HttpPost("import/prices/stop")]
    [ValidateAntiForgeryToken]
    public IActionResult StopPrices()
    {
        _admin.StopPriceSync();
        return Ok(new { message = "Зупинка синхронізації…" });
    }
}
