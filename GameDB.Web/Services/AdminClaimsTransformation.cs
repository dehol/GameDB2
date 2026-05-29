using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace GameDB.Web.Services;

/// <summary>
/// Додає роль Admin до сесії, якщо UserId є в Admin:UserIds (без повторного входу).
/// </summary>
public sealed class AdminClaimsTransformation(AdminUserService adminUsers) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Task.FromResult(principal);

        if (principal.IsInRole(AuthCookieService.AdminRole))
            return Task.FromResult(principal);

        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId) || !adminUsers.IsAdmin(userId))
            return Task.FromResult(principal);

        if (principal.Identity is not ClaimsIdentity identity)
            return Task.FromResult(principal);

        identity.AddClaim(new Claim(ClaimTypes.Role, AuthCookieService.AdminRole));
        return Task.FromResult(principal);
    }
}
