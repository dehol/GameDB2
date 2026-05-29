namespace GameDB.Application.DTOs;

// ─── Фільтри каталогу ─────────────────────────────────────────────────────

public record CatalogFilterDto
{
    // Текстовий пошук по назві гри
    public string? Search { get; init; }

    // Фільтр по жанрах (OR — гра повинна мати хоча б один з обраних)
    public List<int> GenreIds { get; init; } = [];

    // Фільтр по розробнику
    public int? DeveloperId { get; init; }

    // Фільтр по видавцю
    public int? PublisherId { get; init; }

    // Фільтр по магазину (хоча б один оффер у цьому магазині)
    public int? ShopId { get; init; }

    // Ціновий діапазон (за FinalPrice — ціна з урахуванням знижки)
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }

    // Мінімальний відсоток знижки
    public int? MinDiscount { get; init; }

    // Роки випуску
    public int? YearFrom { get; init; }
    public int? YearTo   { get; init; }

    // Мінімальний рейтинг (0–100)
    public double? MinRating { get; init; }

    // Тільки безкоштовні ігри
    public bool? IsFree { get; init; }

    // Сортування
    public CatalogSortBy SortBy   { get; init; } = CatalogSortBy.Rating;
    public bool          SortDesc { get; init; } = true;

    // Пагінація
    public int Page     { get; init; } = 1;
    public int PageSize { get; init; } = 24;
}

public enum CatalogSortBy
{
    Name,
    ReleaseDate,
    Rating,
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
    // Найкращий оффер (найнижча фінальна ціна)
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
    int      GameId,
    string   Name,
    string?  Description,
    string?  HeaderImage,
    string?  IconImage,
    DateOnly? ReleaseDate,
    double?  Rating,
    int?     RatingCount,
    string?  DeveloperName,
    string?  PublisherName,
    List<string> Genres,
    int?     SteamAppId,
    List<GameOfferDto> Offers
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

// ─── Дані для sidebar (списки для фільтрів) ────────────────────────────

public record CatalogSidebarDto(
    List<GenreFilterItemDto>    Genres,
    List<DeveloperFilterItemDto> Developers,
    List<PublisherFilterItemDto> Publishers,
    List<ShopFilterItemDto>     Shops,
    int MinYear,
    int MaxYear,
    decimal MaxPrice
);

public record GenreFilterItemDto(int GenreId, string Name, int GameCount);
public record DeveloperFilterItemDto(int DeveloperId, string Name);
public record PublisherFilterItemDto(int PublisherId, string Name);
public record ShopFilterItemDto(int ShopId, string Name);

// ─── Відповідь каталогу ───────────────────────────────────────────────────

public record CatalogResultDto(
    List<CatalogGameDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);