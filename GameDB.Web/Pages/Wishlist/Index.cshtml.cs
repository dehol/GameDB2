using System.Security.Claims;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Wishlist;

[Authorize(Roles = "User")]
public class IndexModel : PageModel
{
    private readonly IUserCollectionService _collections;

    public IndexModel(IUserCollectionService collections) => _collections = collections;

    public List<UserGameListItemDto> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Items = await _collections.GetWishlistAsync(GetUserId());
    }

    public async Task<IActionResult> OnPostImportSteamAsync()
    {
        var result = await _collections.ImportSteamWishlistAsync(GetUserId());
        SetImportMessage(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int gameId)
    {
        await _collections.RemoveFromWishlistAsync(GetUserId(), gameId);
        TempData["CollectionSuccess"] = "Гру видалено з wishlist.";
        return RedirectToPage();
    }

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private void SetImportMessage(ImportResultDto result)
    {
        if (!result.Success)
            TempData["CollectionError"] = result.Error;
        else
            TempData["CollectionSuccess"] =
                $"Імпортовано {result.Added} ігор. Пропущено {result.SkippedNotInDb} (немає в каталозі GameDB). Усього в Steam: {result.TotalFromSteam}.";
    }
}
