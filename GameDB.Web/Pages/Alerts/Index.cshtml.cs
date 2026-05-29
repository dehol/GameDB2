using System.Security.Claims;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Alerts;

[Authorize(Roles = "User")]
public class IndexModel : PageModel
{
    private readonly IUserCollectionService _collections;

    public IndexModel(IUserCollectionService collections) => _collections = collections;

    public List<AlertListItemDto> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Items = await _collections.GetAlertsAsync(GetUserId());
    }

    public async Task<IActionResult> OnPostDeleteAsync(int alertId)
    {
        try
        {
            await _collections.DeleteAlertAsync(GetUserId(), alertId);
            TempData["CollectionSuccess"] = "Алерт видалено.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["CollectionError"] = ex.Message;
        }
        return RedirectToPage();
    }

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
