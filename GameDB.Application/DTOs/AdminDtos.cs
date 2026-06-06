using GameDB.Application.Services.Import;
using GameDB.Domain.Enums;
namespace GameDB.Application.DTOs;

public enum AdminGameCoverageFilter
{
    All,
    StatusBasic,
    StatusFull,
    NoPrice,
    HasPrice,
    NoExternalId,        // ігри без жодного ExternalId
    BasicNoPrice         // Basic + немає GameOffers
}

public sealed record AdminStatsDto(
    int TotalGames,
    int StatusBasic,
    int StatusFull,
    int WithPrice,
    int WithoutPrice,
    int BasicWithoutPrice,       // Basic-ігри без ціни — черга для price sync
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
    int? Total = null)
{
    // Автоматичний мапінг із бізнес-класу стану в простий DTO для фронтенду
    public static ImportJobStatusDto FromState(ImportOperationState state, string defaultSource)
    {
        return new ImportJobStatusDto(
            IsRunning:     state.IsRunning,
            Source:        state.CurrentProvider ?? defaultSource,
            StartedAt:     state.StartedAt,
            FinishedAt:    state.FinishedAt,
            LastBatchSize: state.BatchSize,
            LastMessage:   state.LastMessage,
            LastError:     state.LastError,
            Processed:     state.Processed,
            Total:         state.Total
        );
    }
}

public record AdminDashboardDto(
    AdminStatsDto Stats,
    ImportJobStatusDto GameEnrichment,
    ImportJobStatusDto PriceSync);

public sealed record AdminGameRowDto(
    int     GameId,
    string  Name,
    GameImportStatus ImportStatus,
    bool    HasPrice,
    DateTime? LastSyncedAt,
    double? Rating);

public sealed record AdminGameListDto(
    List<AdminGameRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    AdminGameCoverageFilter Filter);
