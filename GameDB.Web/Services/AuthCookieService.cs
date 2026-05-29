using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace GameDB.Web.Services;

public sealed class AuthCookieService
{
    public const string GuestRole = "Guest";
    public Task SignInUserAsync(HttpContext http, int userId, string username, bool persistent = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name,           username),
            new(ClaimTypes.Role,           "User"),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = persistent,
                ExpiresUtc   = persistent ? DateTimeOffset.UtcNow.AddDays(30) : null,
            });
    }

    public Task SignInGuestAsync(HttpContext http, string guestId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"guest_{guestId}"),
            new(ClaimTypes.Name,           "Гість"),
            new(ClaimTypes.Role,           GuestRole),
            new("GuestId",                 guestId),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });
    }
}
