using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDB.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthCookieService.AdminRole)]
public class GamesController : ControllerBase
{
    private readonly SteamImportService _steamImportService;
    private readonly IAdminService _admin;

    public GamesController(SteamImportService steamImportService, IAdminService admin)
    {
        _steamImportService = steamImportService;
        _admin = admin;
    }

    [HttpPost("steam/basic-import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportBasic()
    {
        var imported = await _steamImportService.ImportBasicGamesAsync();
        return Ok(new { imported });
    }

    [HttpPost("steam/details/start")]
    [ValidateAntiForgeryToken]
    public IActionResult StartDetailsImport([FromQuery] bool overwrite = false)
    {
        _admin.StartEnrichmentImport(overwrite);
        return Accepted(new { message = "Збагачення SteamSpy запущено." });
    }

    [HttpPost("steam/details/stop")]
    [ValidateAntiForgeryToken]
    public IActionResult StopDetailsImport()
    {
        _admin.StopEnrichmentImport();
        return Ok(new { message = "Збагачення зупинено." });
    }
}
