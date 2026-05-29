using System.Security.Claims;
using GameDB.Application.DTOs.Auth;
using GameDB.Application.Services;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Profile;

[Authorize(Roles = "User")]
public class IndexModel : PageModel
{
    private readonly AuthService _auth;
    private readonly SteamPlayerService _steamPlayers;

    public IndexModel(AuthService auth, SteamPlayerService steamPlayers)
    {
        _auth = auth;
        _steamPlayers = steamPlayers;
    }

    public UserProfileDto Profile { get; private set; } = null!;
    public SteamPlayerInfoDto? SteamInfo { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return RedirectToPage("/Auth/Login");

        var profile = await _auth.GetProfileAsync(userId);
        if (profile is null)
            return NotFound();

        Profile = profile;

        if (!string.IsNullOrEmpty(profile.SteamId))
            SteamInfo = await _steamPlayers.GetPlayerAsync(profile.SteamId);

        return Page();
    }
}
