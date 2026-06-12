using GameDB.Application.DTOs;
using System.Globalization;

namespace GameDB.Web.Catalog;

/// <summary>
/// Стан фільтрів каталогу з query string — єдине джерело для форми та побудови URL.
/// Незмінний record: кожен with-вираз повертає нову копію, без мутацій.
/// </summary>
public sealed record CatalogQuery
{
    public string?   Search      { get; init; }
    public List<int> Genres      { get; init; } = [];
    public List<int> Tags        { get; init; } = [];   // фільтр по тегах
    public int?      DeveloperId { get; init; }
    public int?      PublisherId { get; init; }
    public int?      ShopId      { get; init; }
    public decimal?  MinPrice    { get; init; }
    public decimal?  MaxPrice    { get; init; }
    public int?      MinDiscount { get; init; }
    public int?      YearFrom    { get; init; }
    public int?      YearTo      { get; init; }
    public double?   MinRating   { get; init; }
    public bool?     IsFree      { get; init; }
    public CatalogSortBy SortBy  { get; init; } = CatalogSortBy.Popularity;
    public bool      SortDesc    { get; init; } = true;
    public int       CurrentPage { get; init; } = 1;
    public int       PageSize    { get; init; } = 24;

    // ─── Конвертація в DTO для репозиторію ────────────────────────────────────

    public CatalogFilterDto ToFilterDto() => new()
    {
        Search      = Search,
        GenreIds    = Genres,
        TagIds      = Tags,
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
        Page        = Math.Max(1, CurrentPage),
        PageSize    = PageSize is >= 12 and <= 96 ? PageSize : 24,
    };

    // ─── Побудова URL ─────────────────────────────────────────────────────────

    public string ToCatalogUrl(
        int?          page     = null,
        CatalogSortBy? sortBy  = null,
        bool?          sortDesc = null)
    {
        var pairs = new List<(string Key, string Value)>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                pairs.Add((key, value));
        }

        Add("search", Search);

        foreach (var g in Genres)
            pairs.Add(("genres", g.ToString()));

        foreach (var t in Tags)
            pairs.Add(("tags", t.ToString()));

        Add("developerId", DeveloperId?.ToString());
        Add("publisherId", PublisherId?.ToString());
        Add("shopId",      ShopId?.ToString());
        Add("minPrice",    MinPrice?.ToString(CultureInfo.InvariantCulture));
        Add("maxPrice",    MaxPrice?.ToString(CultureInfo.InvariantCulture));
        Add("minDiscount", MinDiscount?.ToString());
        Add("yearFrom",    YearFrom?.ToString());
        Add("yearTo",      YearTo?.ToString());
        Add("minRating",   MinRating?.ToString(CultureInfo.InvariantCulture));

        if (IsFree == true)
            pairs.Add(("isFree", "true"));

        var sb = sortBy  ?? SortBy;
        var sd = sortDesc ?? SortDesc;
        pairs.Add(("sortBy",   sb.ToString()));
        pairs.Add(("sortDesc", sd.ToString().ToLowerInvariant()));
        pairs.Add(("page",     (page ?? CurrentPage).ToString()));
        pairs.Add(("pageSize", PageSize.ToString()));

        var qs = string.Join("&", pairs.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return $"/Catalog?{qs}";
    }

    public string SortUrl(CatalogSortBy sortBy)
    {
        var newDesc = SortBy == sortBy ? !SortDesc : true;
        return ToCatalogUrl(page: 1, sortBy: sortBy, sortDesc: newDesc);
    }

    public string PageUrl(int page) => ToCatalogUrl(page: page);

    // ─── Видалення окремих фільтрів ───────────────────────────────────────────

    /// <summary>
    /// Повертає URL без одного або кількох іменованих фільтрів (page скидається до 1).
    /// Підтримує: search, developerId, publisherId, shopId, minPrice, maxPrice,
    /// minDiscount, yearFrom, yearTo, minRating, isFree, tags (очищає весь список тегів).
    /// </summary>
    public string Without(params string[] keys)
    {
        var remove = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var q = Copy();

        if (remove.Contains("search"))      q = q with { Search      = null };
        if (remove.Contains("developerId")) q = q with { DeveloperId = null };
        if (remove.Contains("publisherId")) q = q with { PublisherId = null };
        if (remove.Contains("shopId"))      q = q with { ShopId      = null };
        if (remove.Contains("minPrice"))    q = q with { MinPrice    = null };
        if (remove.Contains("maxPrice"))    q = q with { MaxPrice    = null };
        if (remove.Contains("minDiscount")) q = q with { MinDiscount = null };
        if (remove.Contains("yearFrom"))    q = q with { YearFrom    = null };
        if (remove.Contains("yearTo"))      q = q with { YearTo      = null };
        if (remove.Contains("minRating"))   q = q with { MinRating   = null };
        if (remove.Contains("isFree"))      q = q with { IsFree      = null };
        if (remove.Contains("tags"))        q = q with { Tags        = []   };

        return q.ToCatalogUrl(page: 1);
    }

    /// <summary>Повертає URL без конкретного жанру зі списку genres[].</summary>
    public string WithoutGenre(int genreId)
    {
        var genres = Genres.Where(g => g != genreId).ToList();
        return (Copy() with { Genres = genres, CurrentPage = 1 }).ToCatalogUrl();
    }

    /// <summary>Повертає URL без конкретного тега зі списку tags[].</summary>
    public string WithoutTag(int tagId)
    {
        var tags = Tags.Where(t => t != tagId).ToList();
        return (Copy() with { Tags = tags, CurrentPage = 1 }).ToCatalogUrl();
    }

    // ─── Глибока копія ────────────────────────────────────────────────────────

    // with-вирази для record-типів копіюють посилання на List<int>,
    // тому для Genres і Tags потрібно явно клонувати колекції.
    private CatalogQuery Copy() => this with
    {
        Genres = [..Genres],
        Tags   = [..Tags],
    };
}
