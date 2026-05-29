using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Catalog;

public class IndexModel : PageModel
{
    private readonly ICatalogService _catalog;

    public IndexModel(ICatalogService catalog) => _catalog = catalog;

    // ─── Дані для сторінки ────────────────────────────────────────────────
    public CatalogResultDto  Result  { get; private set; } = null!;
    public CatalogSidebarDto Sidebar { get; private set; } = null!;

    // ─── Bind-параметри фільтрів (з query string) ─────────────────────────
    [BindProperty(SupportsGet = true)] public string?       Search      { get; set; }
    [BindProperty(SupportsGet = true)] public List<int>     Genres      { get; set; } = [];
    [BindProperty(SupportsGet = true)] public int?          DeveloperId { get; set; }
    [BindProperty(SupportsGet = true)] public int?          PublisherId { get; set; }
    [BindProperty(SupportsGet = true)] public int?          ShopId      { get; set; }
    [BindProperty(SupportsGet = true)] public decimal?      MinPrice    { get; set; }
    [BindProperty(SupportsGet = true)] public decimal?      MaxPrice    { get; set; }
    [BindProperty(SupportsGet = true)] public int?          MinDiscount { get; set; }
    [BindProperty(SupportsGet = true)] public int?          YearFrom    { get; set; }
    [BindProperty(SupportsGet = true)] public int?          YearTo      { get; set; }
    [BindProperty(SupportsGet = true)] public double?       MinRating   { get; set; }
    [BindProperty(SupportsGet = true)] public bool?         IsFree      { get; set; }
    [BindProperty(SupportsGet = true)] public CatalogSortBy SortBy      { get; set; } = CatalogSortBy.Rating;
    [BindProperty(SupportsGet = true)] public bool          SortDesc    { get; set; } = true;
    [BindProperty(SupportsGet = true)] public int           Page        { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int           PageSize    { get; set; } = 24;

    public async Task OnGetAsync()
    {
        var filter = new CatalogFilterDto
        {
            Search      = Search,
            GenreIds    = Genres,
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
            Page        = Math.Max(1, Page),
            PageSize    = PageSize is >= 12 and <= 96 ? PageSize : 24,
        };

        // EF Core DbContext не є thread-safe — виконуємо послідовно
        Result  = await _catalog.GetCatalogAsync(filter);
        Sidebar = await _catalog.GetSidebarDataAsync();
    }

    // ─── Хелпер: побудова URL пагінації зі збереженням поточних фільтрів ──
    public string PageUrl(int page) => BuildUrl(new Dictionary<string, string?> { ["page"] = page.ToString() });

    public string SortUrl(CatalogSortBy sortBy)
    {
        var newDesc = SortBy == sortBy ? !SortDesc : true;
        return BuildUrl(new Dictionary<string, string?> {
            ["sortBy"]   = sortBy.ToString(),
            ["sortDesc"] = newDesc.ToString().ToLower(),
            ["page"]     = "1"
        });
    }

    private string BuildUrl(Dictionary<string, string?> overrides)
    {
        var qs = new Dictionary<string, string?>(Request.Query
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString()));
        foreach (var (k, v) in overrides)
        {
            if (v is null) qs.Remove(k);
            else qs[k] = v;
        }
        var queryString = string.Join("&", qs.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value ?? "")}"));
        return $"/Catalog?{queryString}";
    }
}
