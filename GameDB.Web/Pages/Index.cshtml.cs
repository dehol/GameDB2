using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages;

public class HomeModel : PageModel
{
    public IActionResult OnGet() => Redirect("/Catalog");
}
