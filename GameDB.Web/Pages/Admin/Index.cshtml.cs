using GameDB.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Admin;

[Authorize(Roles = AuthCookieService.AdminRole)]
public class IndexModel : PageModel
{
    public void OnGet() { }
}
