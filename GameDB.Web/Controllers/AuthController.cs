using System.Security.Claims;
using GameDB.Application.DTOs.Auth;
using GameDB.Application.Services;
using GameDB.Infrastructure.Steam;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameDB.Web.Controllers;

[Route("auth")]
public class AuthController : Controller
{
    // Ім'я куки, що зберігає GuestId на пристрої (1 рік)
    private const string GuestDeviceCookie = "gamedb_guest_id";
    private const string SteamLinkUserCookie = "gamedb_steam_link_uid";
    // Claim role для гостя — використовується в Policy
    public const string GuestRole = AuthCookieService.GuestRole;

    private readonly AuthService _auth;
    private readonly AuthCookieService _cookies;
    private readonly SteamOpenIdService _steamOpenId;
    private readonly SteamPlayerService _steamPlayers;
    private readonly IConfiguration _config;

    public AuthController(
        AuthService auth,
        AuthCookieService cookies,
        SteamOpenIdService steamOpenId,
        SteamPlayerService steamPlayers,
        IConfiguration config)
    {
        _auth = auth;
        _cookies = cookies;
        _steamOpenId = steamOpenId;
        _steamPlayers = steamPlayers;
        _config = config;
    }

    // Email реєстрація / логін — Pages/Auth/Register.cshtml, Login.cshtml (OnPost)

    // ─── Гостьовий вхід ─────────────────────────────────────────────────────
    // GuestId береться з куки пристрою (або генерується новий і зберігається на рік).
    // Гість отримує сесійну auth-куку з роллю "Guest".

    [HttpPost("guest")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuestLogin(string? returnUrl)
    {
        var guestId = GetOrCreateGuestDeviceId();

        await _cookies.SignInGuestAsync(HttpContext, guestId);
        return LocalRedirect(returnUrl ?? "/Catalog");
    }

    // ─── Вихід ──────────────────────────────────────────────────────────────

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }

    // ─── Steam OpenID: крок 1 — редірект на Steam ───────────────────────────

    [HttpGet("steam/login")]
    public IActionResult SteamLogin(string? returnUrl)
    {
        Response.Cookies.Delete(SteamLinkUserCookie);

        if (!string.IsNullOrEmpty(returnUrl))
            Response.Cookies.Append("auth_return_url", returnUrl,
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10) });

        return Redirect(BuildSteamOpenIdUrl());
    }

    /// <summary>Прив'язка Steam до вже авторизованого акаунта (email/пароль).</summary>
    [HttpGet("steam/link")]
    [Authorize(Roles = "User")]
    public IActionResult SteamLink()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return RedirectToPage("/Auth/Login");

        Response.Cookies.Append(SteamLinkUserCookie, userId, new CookieOptions
        {
            HttpOnly  = true,
            SameSite  = SameSiteMode.Lax,
            MaxAge    = TimeSpan.FromMinutes(10),
            IsEssential = true,
        });

        return Redirect(BuildSteamOpenIdUrl());
    }

    [HttpPost("steam/unlink")]
    [Authorize(Roles = "User")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SteamUnlink()
    {
        if (!TryGetUserId(out var userId))
            return RedirectToPage("/Auth/Login");

        var result = await _auth.UnlinkSteamAsync(userId);
        if (!result.Success)
            TempData["ProfileError"] = result.Error;
        else
            TempData["ProfileSuccess"] = "Steam успішно відв'язано.";

        return LocalRedirect("/Profile");
    }

    // ─── Steam OpenID: крок 2 — колбек від Steam ────────────────────────────

    [HttpGet("steam/callback")]
    public async Task<IActionResult> SteamCallback()
    {
        var queryDict = Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        var steamId   = await _steamOpenId.ValidateCallbackAsync(queryDict);

        if (steamId is null)
        {
            TempData["AuthError"] = "Не вдалося підтвердити вхід через Steam. Спробуйте ще раз.";
            return RedirectToPage("/Auth/Login");
        }

        // Режим прив'язки Steam до існуючого акаунта
        if (Request.Cookies.TryGetValue(SteamLinkUserCookie, out var linkUserId)
            && int.TryParse(linkUserId, out var uid))
        {
            Response.Cookies.Delete(SteamLinkUserCookie);

            if (!TryGetUserId(out var currentUserId) || currentUserId != uid)
            {
                TempData["AuthError"] = "Сесія закінчилась. Увійдіть знову та повторіть прив'язку Steam.";
                return RedirectToPage("/Auth/Login");
            }

            var linkResult = await _auth.LinkSteamAsync(uid, steamId);
            if (!linkResult.Success)
                TempData["ProfileError"] = linkResult.Error;
            else
                TempData["ProfileSuccess"] = "Steam успішно прив'язано.";

            return LocalRedirect("/Profile");
        }

        var steamPlayer = await _steamPlayers.GetPlayerAsync(steamId);
        var steamName   = steamPlayer?.PersonaName;
        var result      = await _auth.LoginOrRegisterViaSteamAsync(steamId, steamName);

        if (!result.Success)
        {
            TempData["AuthError"] = result.Error;
            return RedirectToPage("/Auth/Login");
        }

        await _cookies.SignInUserAsync(HttpContext, result.UserId!.Value, result.Username!);

        var returnUrl = Request.Cookies["auth_return_url"];
        Response.Cookies.Delete("auth_return_url");
        return LocalRedirect(returnUrl ?? "/Catalog");
    }

    // ─── Допоміжні ──────────────────────────────────────────────────────────

    /// <summary>
    /// Читає GuestId з куки пристрою або генерує новий і зберігає на 1 рік.
    /// </summary>
    private string GetOrCreateGuestDeviceId()
    {
        if (Request.Cookies.TryGetValue(GuestDeviceCookie, out var existing)
            && !string.IsNullOrWhiteSpace(existing))
            return existing;

        var newId = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(GuestDeviceCookie, newId, new CookieOptions
        {
            HttpOnly  = true,
            SameSite  = SameSiteMode.Lax,
            MaxAge    = TimeSpan.FromDays(365),
            IsEssential = true,   // не блокується GDPR-банером
        });
        return newId;
    }

    private string BuildSteamOpenIdUrl()
    {
        var callbackUrl = _config["Steam:OpenId:ReturnUrl"]
            ?? $"{Request.Scheme}://{Request.Host}/auth/steam/callback";
        var realm = _config["Steam:OpenId:Realm"]
            ?? $"{Request.Scheme}://{Request.Host}";
        return _steamOpenId.BuildRedirectUrl(callbackUrl, realm);
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(raw) && int.TryParse(raw, out userId);
    }
}
