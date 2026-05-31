namespace GameDB.Application.DTOs;

public record UserGameListItemDto(
    int      GameId,
    string   Name,
    string?  HeaderImage,
    double?  Rating,
    decimal? BestFinalPrice,
    string?  BestCurrency,
    int      BestDiscount,
    DateTime AddedAt,
    /// <summary>ExternalId з магазину Steam (рядок, напр. "730"). Null якщо гра не має Steam-запису.</summary>
    string?  SteamExternalId
);

public record UserLibraryItemDto(
    int      GameId,
    string   Name,
    string?  HeaderImage,
    double?  Rating,
    decimal? BestFinalPrice,
    string?  BestCurrency,
    int      BestDiscount,
    DateTime AddedAt,
    int      ShopId,
    string   ShopName,
    /// <summary>ExternalId з магазину Steam (рядок, напр. "730"). Null якщо гра не має Steam-запису.</summary>
    string?  SteamExternalId
);

public record AlertListItemDto(
    int AlertId,
    int GameId,
    string GameName,
    string? HeaderImage,
    decimal? TargetPrice,
    decimal? CurrentBestPrice,
    string? Currency,
    DateTime CreatedAt,
    DateTime? TriggeredAt,
    bool IsActive
);

public record CreateAlertDto
{
    public int GameId { get; init; }
    public decimal TargetPrice { get; init; }
}

public record ImportResultDto(
    bool Success,
    string? Error,
    int Added,
    int SkippedNotInDb,
    int TotalFromSteam
)
{
    public static ImportResultDto Fail(string error) => new(false, error, 0, 0, 0);
    public static ImportResultDto Ok(int added, int skipped, int total) =>
        new(true, null, added, skipped, total);
}

public record GameCollectionStateDto(
    bool InWishlist,
    bool InLibrary,
    IReadOnlyList<AlertListItemDto> Alerts
);
