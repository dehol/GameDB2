using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages.Catalog;

public class IndexModel : PageModel
{
    private readonly ICatalogService _catalog;

    public IndexModel(ICatalogService catalog)
    {
        _catalog = catalog;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int CurrentPage { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public List<int> GenreIds { get; set; } = [];
    [BindProperty(SupportsGet = true)] public List<int> TagIds { get; set; } = [];
    [BindProperty(SupportsGet = true)] public int? DeveloperId { get; set; }
    [BindProperty(SupportsGet = true)] public int? PublisherId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ShopId { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MinPrice { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MaxPrice { get; set; }
    [BindProperty(SupportsGet = true)] public int? MinDiscount { get; set; }
    [BindProperty(SupportsGet = true)] public int? YearFrom { get; set; }
    [BindProperty(SupportsGet = true)] public int? YearTo { get; set; }
    [BindProperty(SupportsGet = true)] public double? MinRating { get; set; }
    [BindProperty(SupportsGet = true)] public bool? IsFree { get; set; }
    [BindProperty(SupportsGet = true)] public CatalogSortBy SortBy { get; set; } = CatalogSortBy.Popularity;
    [BindProperty(SupportsGet = true)] public bool SortDesc { get; set; } = true;

    public CatalogResultDto? Result { get; private set; }
    public CatalogSidebarDto? Sidebar { get; private set; }

    public async Task OnGetAsync()
    {
        if (CurrentPage < 1) CurrentPage = 1;

        var filter = new CatalogFilterDto
        {
            Search = Search,
            GenreIds = GenreIds,
            TagIds = TagIds,
            DeveloperId = DeveloperId,
            PublisherId = PublisherId,
            ShopId = ShopId,
            MinPrice = MinPrice,
            MaxPrice = MaxPrice,
            MinDiscount = MinDiscount,
            YearFrom = YearFrom,
            YearTo = YearTo,
            MinRating = MinRating,
            IsFree = IsFree,
            SortBy = SortBy,
            SortDesc = SortDesc,
            Page = CurrentPage,
            PageSize = 24
        };

        Result = await _catalog.GetCatalogAsync(filter);
        Sidebar = await _catalog.GetSidebarDataAsync();
    }

    public string GetPageUrl(int page)
    {
        var queryParams = new List<string> { $"currentPage={page}" };

        if (!string.IsNullOrEmpty(Search)) queryParams.Add($"search={Uri.EscapeDataString(Search)}");
        if (DeveloperId.HasValue) queryParams.Add($"developerId={DeveloperId}");
        if (PublisherId.HasValue) queryParams.Add($"publisherId={PublisherId}");
        if (ShopId.HasValue) queryParams.Add($"shopId={ShopId}");
        if (MinPrice.HasValue) queryParams.Add($"minPrice={MinPrice}");
        if (MaxPrice.HasValue) queryParams.Add($"maxPrice={MaxPrice}");
        if (MinDiscount.HasValue) queryParams.Add($"minDiscount={MinDiscount}");
        if (YearFrom.HasValue) queryParams.Add($"yearFrom={YearFrom}");
        if (YearTo.HasValue) queryParams.Add($"yearTo={YearTo}");
        if (MinRating.HasValue) queryParams.Add($"minRating={MinRating}");
        if (IsFree.HasValue) queryParams.Add($"isFree={IsFree.Value.ToString().ToLower()}");
        
        queryParams.Add($"sortBy={SortBy}");
        queryParams.Add($"sortDesc={SortDesc.ToString().ToLower()}");

        foreach (var id in GenreIds) queryParams.Add($"genreIds={id}");
        foreach (var id in TagIds) queryParams.Add($"tagIds={id}");

        return "?" + string.Join("&", queryParams);
    }
}