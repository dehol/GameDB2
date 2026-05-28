using System.Security.Claims;
using GameDB.Application.DTOs.Auth;
using GameDB.Application.Services;
using GameDB.Infrastructure.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace GameDB.Web.Controllers;

[Route("auth")]
public class AuthController : Controller
{
    // Ім'я куки, що зберігає GuestId на пристрої (1 рік)
    private const string GuestDeviceCookie = "gamedb_guest_id";
    // Claim role для гостя — використовується в Policy
    public const string GuestRole = "Guest";

    private readonly AuthService _auth;
    private readonly SteamOpenIdService _steamOpenId;
    private readonly IConfiguration _config;

    public AuthController(AuthService auth, SteamOpenIdService steamOpenId, IConfiguration config)
    {
        _auth = auth;
        _steamOpenId = steamOpenId;
        _config = config;
    }

    // ─── Email реєстрація ───────────────────────────────────────────────────

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterDto dto, string? returnUrl)
    {
        if (!ModelState.IsValid)
            return View("~/Pages/Auth/Register.cshtml", dto);

        var result = await _auth.RegisterAsync(dto);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View("~/Pages/Auth/Register.cshtml", dto);
        }

        await SignInAsync(result.UserId!.Value, result.Username!);
        return LocalRedirect(returnUrl ?? "/");
    }

    // ─── Email логін ────────────────────────────────────────────────────────

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginDto dto, string? returnUrl)
    {
        if (!ModelState.IsValid)
            return View("~/Pages/Auth/Login.cshtml", dto);

        var result = await _auth.LoginAsync(dto);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View("~/Pages/Auth/Login.cshtml", dto);
        }

        await SignInAsync(result.UserId!.Value, result.Username!, dto.RememberMe);
        return LocalRedirect(returnUrl ?? "/");
    }

    // ─── Гостьовий вхід ─────────────────────────────────────────────────────
    // GuestId береться з куки пристрою (або генерується новий і зберігається на рік).
    // Гість отримує сесійну auth-куку з роллю "Guest".

    [HttpPost("guest")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuestLogin(string? returnUrl)
    {
        var guestId = GetOrCreateGuestDeviceId();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"guest_{guestId}"),
            new(ClaimTypes.Name,           "Гість"),
            new(ClaimTypes.Role,           GuestRole),
            new("GuestId",                 guestId),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // Auth-кука — сесійна (закривається з браузером).
        // GuestId-кука на пристрої — постійна (1 рік).
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });

        return LocalRedirect(returnUrl ?? "/");
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
        if (!string.IsNullOrEmpty(returnUrl))
            Response.Cookies.Append("auth_return_url", returnUrl,
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10) });

        var callbackUrl = _config["Steam:OpenId:ReturnUrl"]
            ?? $"{Request.Scheme}://{Request.Host}/auth/steam/callback";
        var realm = _config["Steam:OpenId:Realm"]
            ?? $"{Request.Scheme}://{Request.Host}";

        return Redirect(_steamOpenId.BuildRedirectUrl(callbackUrl, realm));
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

        var steamName = await TryGetSteamNameAsync(steamId);
        var result    = await _auth.LoginOrRegisterViaSteamAsync(steamId, steamName);

        if (!result.Success)
        {
            TempData["AuthError"] = result.Error;
            return RedirectToPage("/Auth/Login");
        }

        await SignInAsync(result.UserId!.Value, result.Username!);

        var returnUrl = Request.Cookies["auth_return_url"];
        Response.Cookies.Delete("auth_return_url");
        return LocalRedirect(returnUrl ?? "/");
    }

    // ─── Допоміжні ──────────────────────────────────────────────────────────

    private async Task SignInAsync(int userId, string username, bool persistent = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name,           username),
            new(ClaimTypes.Role,           "User"),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = persistent,
                ExpiresUtc   = persistent ? DateTimeOffset.UtcNow.AddDays(30) : null,
            });
    }

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

    private async Task<string?> TryGetSteamNameAsync(string steamId)
    {
        var apiKey = _config["Steam:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            using var http = new HttpClient();
            var url  = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={apiKey}&steamids={steamId}";
            var json = await http.GetStringAsync(url);
            var start = json.IndexOf("\"personaname\":\"", StringComparison.Ordinal);
            if (start < 0) return null;
            start += 15;
            var end = json.IndexOf('"', start);
            return end > start ? json[start..end] : null;
        }
        catch { return null; }
    }
}
