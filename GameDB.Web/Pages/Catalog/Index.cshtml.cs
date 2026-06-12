using System.Security.Claims;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Web.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Catalog;

public class IndexModel : PageModel
{
    private readonly ICatalogService        _catalog;
    private readonly IUserCollectionService _collections;

    public IndexModel(ICatalogService catalog, IUserCollectionService collections)
    {
        _catalog     = catalog;
        _collections = collections;
    }

    // ─── Дані для в'юшки ──────────────────────────────────────────────────────

    public CatalogResultDto  Result      { get; private set; } = null!;
    public CatalogSidebarDto Sidebar     { get; private set; } = null!;
    public CatalogQuery      Query       { get; private set; } = null!;
    public HashSet<int>      WishlistIds { get; private set; } = [];
    public bool              IsRegisteredUser { get; private set; }

    // ─── Прив'язка параметрів GET-запиту ─────────────────────────────────────

    [BindProperty(SupportsGet = true)] public string?        Search      { get; set; }
    [BindProperty(SupportsGet = true)] public List<int>      Genres      { get; set; } = [];
    [BindProperty(SupportsGet = true)] public List<int>      Tags        { get; set; } = [];
    [BindProperty(SupportsGet = true)] public int?           DeveloperId { get; set; }
    [BindProperty(SupportsGet = true)] public int?           PublisherId { get; set; }
    [BindProperty(SupportsGet = true)] public int?           ShopId      { get; set; }
    [BindProperty(SupportsGet = true)] public decimal?       MinPrice    { get; set; }
    [BindProperty(SupportsGet = true)] public decimal?       MaxPrice    { get; set; }
    [BindProperty(SupportsGet = true)] public int?           MinDiscount { get; set; }
    [BindProperty(SupportsGet = true)] public int?           YearFrom    { get; set; }
    [BindProperty(SupportsGet = true)] public int?           YearTo      { get; set; }
    [BindProperty(SupportsGet = true)] public double?        MinRating   { get; set; }
    [BindProperty(SupportsGet = true)] public bool?          IsFree      { get; set; }
    [BindProperty(SupportsGet = true)] public CatalogSortBy  SortBy      { get; set; } = CatalogSortBy.Popularity;
    [BindProperty(SupportsGet = true)] public bool           SortDesc    { get; set; } = true;
    [BindProperty(SupportsGet = true)] public int            PageSize    { get; set; } = 24;

    // CurrentPage — не [BindProperty]: читається через параметр page у OnGetAsync,
    // щоб уникнути конфліктів між POST-body та query-string.
    public int CurrentPage { get; private set; } = 1;

    // ─── Обробники GET ────────────────────────────────────────────────────────

    /// <summary>
    /// Повна сторінка — або HTMX partial, якщо запит прийшов від hx-get.
    /// Обидва шляхи виконують LoadCatalogAsync() — Sidebar завантажується завжди,
    /// бо він потрібен для active-filter chips у partial.
    /// Рядки даних БД читаються строго послідовно (await), не Task.WhenAll,
    /// щоб не порушити потокобезпечність єдиного AppDbContext у Scoped-контейнері.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(int page = 1)
    {
        CurrentPage = Math.Max(1, page);
        await LoadCatalogAsync();

        // HX-Request: true — автоматичний заголовок, який HTMX додає до кожного hx-get.
        // Повертаємо тільки partial з сіткою ігор — без layout і sidebar.
        if (Request.Headers.ContainsKey("HX-Request"))
            return Partial("_GamesListPartial", this);

        return Page();
    }

    // ─── Wishlist-мутації ─────────────────────────────────────────────────────

    public Task<IActionResult> OnPostAddWishlistAsync(int gameId, string? returnUrl) =>
        MutateAsync(returnUrl, async uid =>
        {
            await _collections.AddToWishlistAsync(uid, gameId);
            TempData["CollectionSuccess"] = "Додано до wishlist.";
        });

    public Task<IActionResult> OnPostRemoveWishlistAsync(int gameId, string? returnUrl) =>
        MutateAsync(returnUrl, async uid =>
        {
            await _collections.RemoveFromWishlistAsync(uid, gameId);
            TempData["CollectionSuccess"] = "Видалено з wishlist.";
        });

    // ─── URL-хелпери (доступні з _GamesListPartial) ──────────────────────────

    public string PageUrl(int page)                   => Query.PageUrl(page);
    public string SortUrl(CatalogSortBy sortBy)       => Query.SortUrl(sortBy);
    public string RemoveFilter(string key)             => Query.Without(key);
    public string RemoveFilters(params string[] keys)  => Query.Without(keys);
    public string RemoveGenre(int genreId)             => Query.WithoutGenre(genreId);
    public string RemoveTag(int tagId)                 => Query.WithoutTag(tagId);
    public string CatalogReturnUrl()                   => Query.ToCatalogUrl();

    // ─── Internal ─────────────────────────────────────────────────────────────

    private async Task LoadCatalogAsync()
    {
        Query = BuildQuery();
        var filter = Query.ToFilterDto();

        // Строго послідовні await — жодного Task.WhenAll через AppDbContext.
        Result  = await _catalog.GetCatalogAsync(filter);
        Sidebar = await _catalog.GetSidebarDataAsync();

        IsRegisteredUser = User.IsInRole("User");
        if (IsRegisteredUser && TryGetUserId(out var userId))
        {
            var wl = await _collections.GetWishlistAsync(userId);
            WishlistIds = wl.Select(x => x.GameId).ToHashSet();
        }
    }

    private async Task<IActionResult> MutateAsync(string? returnUrl, Func<int, Task> action)
    {
        if (!TryGetUserId(out var userId))
            return Challenge();

        Query = BuildQuery();

        try
        {
            await action(userId);
        }
        catch (InvalidOperationException ex)
        {
            TempData["CollectionError"] = ex.Message;
        }

        return LocalRedirect(
            string.IsNullOrWhiteSpace(returnUrl) ? CatalogReturnUrl() : returnUrl);
    }

    private CatalogQuery BuildQuery() => new()
    {
        Search      = Search,
        Genres      = Genres,
        Tags        = Tags,
        DeveloperId = DeveloperId,
        PublisherId = PublisherId,
        ShopId      = ShopId,
        MinPrice    = MinPrice,
        MaxPrice    = MaxPrice,
        MinDiscount = MinDiscount,
        YearFrom    = YearFrom,
        YearTo      = YearTo,
        MinRating   = MinRating,
        IsFree      = IsFree,
        SortBy      = SortBy,
        SortDesc    = SortDesc,
        CurrentPage = CurrentPage,
        PageSize    = PageSize,
    };

    private bool TryGetUserId(out int userId)
    {
        userId = 0;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return User.IsInRole("User")
               && !string.IsNullOrEmpty(raw)
               && int.TryParse(raw, out userId);
    }
}
