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

    public GamesController(SteamImportService steamImportService)
    {
        _steamImportService = steamImportService;
    }

    /// <summary>
    /// Perform basic import: fetch Steam app list and insert new games (minimal info).
    /// </summary>
    [HttpPost("steam/basic-import")]
    public async Task<IActionResult> ImportBasic()
    {
        var imported = await _steamImportService.ImportBasicGamesAsync();
        return Ok(new { imported });
    }

    /// <summary>
    /// Start background worker to import detailed info for games.
    /// Uses the singleton SteamImportState monitored by the hosted worker.
    /// </summary>
    [HttpPost("steam/details/start")]
    public IActionResult StartDetailsImport([FromQuery] string source = "steam",
        [FromServices] SteamImportState state = null!,
        [FromServices] IgdbImportState? igdbState = null,
        [FromServices] FastIgdbImportService? igdbService = null)
    {
        // Support two sources: steam (background worker) or igdb (fast import)
        if (string.Equals(source, "steam", StringComparison.OrdinalIgnoreCase))
        {
            state.IsImportingDetails = true;
            return Accepted(new { message = "Фоновий процес імпорту деталей (Steam) запущено." });
        }

        if (string.Equals(source, "igdb", StringComparison.OrdinalIgnoreCase))
        {
            if (igdbState == null)
                return BadRequest(new { message = "IGDB state not available." });

            // Start the IGDB background worker which will process the full queue
            igdbState.IsImporting = true;
            return Accepted(new { message = "Фоновий процес імпорту деталей (IGDB) запущено." });
        }

        return BadRequest(new { message = "Unknown source. Use 'steam' or 'igdb'." });
    }

    /// <summary>
    /// Stop background details import.
    /// </summary>
    [HttpPost("steam/details/stop")]
    public IActionResult StopDetailsImport([FromServices] SteamImportState state,
        [FromServices] IgdbImportState? igdbState = null)
    {
        state.IsImportingDetails = false;
        if (igdbState != null) igdbState.IsImporting = false;
        return Ok(new { message = "Фонові процеси імпорту деталей зупинено." });
    }
}
