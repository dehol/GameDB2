using GameDB.Domain.Enums;
namespace GameDB.Application.DTOs;

// ─── Фільтри каталогу ─────────────────────────────────────────────────────

public record CatalogFilterDto
{
    public string? Search { get; init; }
    public List<int> GenreIds { get; init; } = [];
    public List<int> TagIds { get; init; } = []; // <-- ДОДАНО: Фільтр по тегах

    public int? DeveloperId { get; init; }
    public int? PublisherId { get; init; }
    public int? ShopId { get; init; }

    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public int? MinDiscount { get; init; }

    public int? YearFrom { get; init; }
    public int? YearTo   { get; init; }
    public double? MinRating { get; init; }
    public bool? IsFree { get; init; }

    public CatalogSortBy SortBy   { get; init; } = CatalogSortBy.Popularity;
    public bool          SortDesc { get; init; } = true;

    public int Page     { get; init; } = 1;
    public int PageSize { get; init; } = 24;
}

public enum CatalogSortBy
{
    Name,
    ReleaseDate,
    Rating,
    Popularity,
    Price,
    Discount,
    UpdatedAt
}

// ─── Картка гри для каталогу ─────────────────────────────────────────────

public record CatalogGameDto(
    int     GameId,
    string  Name,
    string? HeaderImage,
    string? IconImage,
    DateOnly? ReleaseDate,
    double? Rating,
    int?    RatingCount,
    string? DeveloperName,
    List<string> Genres,
    decimal? BestFinalPrice,
    decimal? BestCurrentPrice,
    int      BestDiscount,
    string?  BestCurrency,
    string?  BestShopName,
    string?  BestDownloadUrl,
    bool     IsFree
);

// ─── Деталі гри ──────────────────────────────────────────────────────────

public record GameDetailDto(
    int       GameId,
    string    Name,
    string?   HeaderImage,
    string?   IconImage,
    DateOnly? ReleaseDate,
    double?   Rating,
    int?      RatingCount,
    string?   DeveloperName,
    string?   PublisherName,
    List<string> Genres,
    List<string> Tags, // Вже було, використовуватимемо в Details.cshtml
    Dictionary<string, string> ExternalIds,
    List<GameOfferDto> Offers,
    GameImportStatus ImportStatus
);

public record GameOfferDto(
    int     GameOfferId,
    string  ShopName,
    string? ShopBaseUrl,
    decimal CurrentPrice,
    decimal? FinalPrice,
    int     Discount,
    string  Currency,
    string? DownloadUrl,
    DateTime? LastSyncedAt
);

// ─── Дані для sidebar ────────────────────────────────────────────────────

public record CatalogSidebarDto(
    List<GenreFilterItemDto>    Genres,
    List<TagFilterItemDto>      Tags, // <-- ДОДАНО: Список тегів для сайдбару
    List<DeveloperFilterItemDto> Developers,
    List<PublisherFilterItemDto> Publishers,
    List<ShopFilterItemDto>     Shops,
    int MinYear,
    int MaxYear,
    decimal MaxPrice
);

public record GenreFilterItemDto(int GenreId, string Name, int GameCount);
public record TagFilterItemDto(int TagId, string Name, int GameCount); // <-- ДОДАНО: Рекорд тегу
public record DeveloperFilterItemDto(int DeveloperId, string Name);
public record PublisherFilterItemDto(int PublisherId, string Name);
public record ShopFilterItemDto(int ShopId, string Name);

public record CatalogResultDto(
    List<CatalogGameDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);