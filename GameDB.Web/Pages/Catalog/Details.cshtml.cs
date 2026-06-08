using System.Security.Claims;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Catalog;

public class DetailsModel : PageModel
{
    private readonly ICatalogService _catalog;
    private readonly IUserCollectionService _collections;
    private readonly IGameAlertService _alerts;
    private readonly GameDescriptionSanitizer _descriptionSanitizer;

    public DetailsModel(
        ICatalogService catalog,
        IUserCollectionService collections,
        IGameAlertService alerts,
        GameDescriptionSanitizer descriptionSanitizer)
    {
        _catalog = catalog;
        _collections = collections;
        _alerts = alerts;
        _descriptionSanitizer = descriptionSanitizer;
    }

    public GameDetailDto Game { get; private set; } = null!;
    public string SanitizedDescription { get; private set; } = string.Empty;
    public GameCollectionStateDto? Collection { get; private set; }
    public GamePriceAlertContextDto? AlertContext { get; private set; }
    public bool IsRegisteredUser { get; private set; }
    public List<ShopPriceHistoryDto> PriceHistory { get; private set; } = [];

    [BindProperty] public decimal TargetPrice { get; set; }
    [BindProperty] public bool AutoUpdate { get; set; }
    [BindProperty] public string AutoUpdateMode { get; set; } = "BeatLowest";
    [BindProperty] public int? ShopId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, bool alert = false)
    {
        var result = await LoadPageAsync(id);
        if (result is not PageResult) return result;
        if (alert) ViewData["OpenAlertModal"] = true;
        return Page();
    }

    public Task<IActionResult> OnPostAddWishlistAsync(int id) => MutateAsync(id, async uid =>
    {
        await _collections.AddToWishlistAsync(uid, id);
        TempData["CollectionSuccess"] = "Додано до wishlist.";
    });

    public Task<IActionResult> OnPostRemoveWishlistAsync(int id) => MutateAsync(id, async uid =>
    {
        await _collections.RemoveFromWishlistAsync(uid, id);
        TempData["CollectionSuccess"] = "Видалено з wishlist.";
    });

    public Task<IActionResult> OnPostSaveAlertAsync(int id) => MutateAsync(id, async uid =>
    {
        var mode = Enum.TryParse<AlertAutoUpdateMode>(AutoUpdateMode, out var m)
            ? m : AlertAutoUpdateMode.BeatLowest;

        await _alerts.SaveAlertAsync(uid, new SavePriceAlertDto
        {
            GameId          = id,
            TargetPrice     = TargetPrice,
            AutoUpdate      = AutoUpdate,
            AutoUpdateMode  = mode,
            ShopId          = ShopId,
        });
        TempData["CollectionSuccess"] = "Price alert збережено.";
    });

    public Task<IActionResult> OnPostDeleteAlertAsync(int id) => MutateAsync(id, async uid =>
    {
        await _alerts.DeleteAlertAsync(uid, id);
        TempData["CollectionSuccess"] = "Price alert видалено.";
    });

    private async Task<IActionResult> MutateAsync(int id, Func<int, Task> action)
    {
        if (!TryGetUserId(out var userId))
            return Challenge();

        try
        {
            await action(userId);
        }
        catch (InvalidOperationException ex)
        {
            TempData["CollectionError"] = ex.Message;
        }

        return Redirect($"/Catalog/Details/{id}");
    }

    private async Task<IActionResult> LoadPageAsync(int id)
    {
        var game = await _catalog.GetGameDetailAsync(id);
        if (game is null)
            return NotFound();

        Game = game;
        PriceHistory = await _catalog.GetPriceHistoryAsync(id);
        IsRegisteredUser = User.IsInRole("User");

        if (IsRegisteredUser && TryGetUserId(out var userId))
        {
            Collection = await _collections.GetCollectionStateAsync(userId, id);
            AlertContext = await _alerts.GetPriceContextAsync(id, userId);
        }

        return Page();
    }

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return User.IsInRole("User") && !string.IsNullOrEmpty(raw) && int.TryParse(raw, out userId);
    }
}
