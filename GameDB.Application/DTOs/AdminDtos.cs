namespace GameDB.Application.DTOs;

public enum AdminGameCoverageFilter
{
    All,
    NoDetails,
    HasDetails,
    NoPrice,
    HasPrice,
    NoSteamAppId,
    SteamNoPrice,
}

public record AdminStatsDto(
    int TotalGames,
    int WithDetails,
    int WithoutDetails,
    int WithSteamAppId,
    int WithPrice,
    int WithoutPrice,
    int SteamWithoutPrice,
    DateTime? LastPriceSyncAt);

public record ImportJobStatusDto(
    bool IsRunning,
    string Source,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int? LastBatchSize,
    string? LastMessage,
    string? LastError,
    int? Processed = null,
    int? Total = null);

public record AdminDashboardDto(
    AdminStatsDto Stats,
    ImportJobStatusDto GameEnrichment,
    ImportJobStatusDto PriceSync,
    int PendingDetailsCount);

public record AdminGameRowDto(
    int GameId,
    string Name,
    int? SteamAppId,
    bool HasDetails,
    bool HasPrice,
    DateTime? LastPriceSyncAt,
    double? Rating);

public record AdminGameListDto(
    IReadOnlyList<AdminGameRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    AdminGameCoverageFilter Filter);
