using GameDB.Application.DTOs.Auth;
using GameDB.Application.Services;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Auth;

public class RegisterModel : PageModel
{
    private readonly AuthService _auth;
    private readonly AuthCookieService _cookies;

    public RegisterModel(AuthService auth, AuthCookieService cookies)
    {
        _auth = auth;
        _cookies = cookies;
    }

    [BindProperty]
    public RegisterDto Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl)
    {
        ReturnUrl = returnUrl ?? "/Catalog";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= "/Catalog";

        if (!ModelState.IsValid)
            return Page();

        var result = await _auth.RegisterAsync(Input);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }

        await _cookies.SignInUserAsync(HttpContext, result.UserId!.Value, result.Username!);
        return LocalRedirect(ReturnUrl);
    }
}
