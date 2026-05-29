namespace GameDB.Application.DTOs;

public enum AlertAutoUpdateMode
{
    MatchLowest,
    BeatLowest,
}

public record PriceAlertShopOptionDto(int ShopId, string ShopName);

public record GamePriceAlertContextDto(
    int GameId,
    string GameName,
    string? HeaderImage,
    string Currency,
    decimal? CurrentLowest,
    decimal? HistoricalLow,
    decimal? BasePrice,
    IReadOnlyList<PriceAlertShopOptionDto> Shops,
    ExistingPriceAlertDto? ExistingAlert
);

public record ExistingPriceAlertDto(
    int AlertId,
    decimal TargetPrice,
    bool AutoUpdate,
    AlertAutoUpdateMode AutoUpdateMode,
    int? ShopId
);

public record SavePriceAlertDto
{
    public int GameId { get; init; }
    public decimal TargetPrice { get; init; }
    public bool AutoUpdate { get; init; }
    public AlertAutoUpdateMode AutoUpdateMode { get; init; } = AlertAutoUpdateMode.BeatLowest;
    public int? ShopId { get; init; }
}
