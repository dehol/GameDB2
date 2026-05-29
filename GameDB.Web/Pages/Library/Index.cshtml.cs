using System.Security.Claims;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Library;

[Authorize(Roles = "User")]
public class IndexModel : PageModel
{
    private readonly IUserCollectionService _collections;

    public IndexModel(IUserCollectionService collections) => _collections = collections;

    public List<UserLibraryItemDto> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Items = await _collections.GetLibraryAsync(GetUserId());
    }

    public async Task<IActionResult> OnPostImportSteamAsync()
    {
        var result = await _collections.ImportSteamLibraryAsync(GetUserId());
        SetImportMessage(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int gameId, int shopId)
    {
        await _collections.RemoveFromLibraryAsync(GetUserId(), gameId, shopId);
        TempData["CollectionSuccess"] = "Гру видалено з бібліотеки.";
        return RedirectToPage();
    }

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private void SetImportMessage(ImportResultDto result)
    {
        if (!result.Success)
            TempData["CollectionError"] = result.Error;
        else
            TempData["CollectionSuccess"] =
                $"Імпортовано {result.Added} ігор. Пропущено {result.SkippedNotInDb} (немає в каталозі). Усього в Steam: {result.TotalFromSteam}.";
    }
}
