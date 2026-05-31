using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository _adminRepo;
    private readonly SteamSpyImportService _steamImport;
    private readonly GameEnrichmentImportState _enrichmentState;
    private readonly PriceSyncState _priceState;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IAdminRepository adminRepo,
        IGameRepository gameRepo,
        SteamSpyImportService steamImport,
        GameEnrichmentImportState enrichmentState,
        PriceSyncState priceState,
        IServiceScopeFactory scopeFactory,
        ILogger<AdminService> logger)
    {
        _adminRepo = adminRepo;
        _steamImport = steamImport;
        _enrichmentState = enrichmentState;
        _priceState = priceState;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        try
        {
            var stats = await _adminRepo.GetStatsAsync(ct);

            return new AdminDashboardDto(
                stats,
                ToStatus(_enrichmentState.IsImporting, "enrichment",
                    _enrichmentState.StartedAt, _enrichmentState.FinishedAt,
                    _enrichmentState.LastBatchSize, _enrichmentState.LastMessage,
                    _enrichmentState.LastError),
                ToStatus(_priceState.IsRunning, "steamspy",
                    _priceState.StartedAt, _priceState.FinishedAt,
                    _priceState.LastBatchSize, _priceState.LastMessage,
                    _priceState.LastError,
                    _priceState.ProcessedGames, _priceState.TotalGames));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
    public Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter,
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default)
        => _adminRepo.GetGamesAsync(filter, page, pageSize, search, ct);

    public Task<int> ImportBasicGamesAsync(CancellationToken ct = default)
    => _steamImport.ImportBasicGamesAsync(ct);

    public void StartEnrichmentImport(bool overwriteExisting = false)
    {
        _enrichmentState.IsImporting = true;
        _enrichmentState.StartedAt = DateTime.UtcNow;
        _enrichmentState.FinishedAt = null;
        _enrichmentState.OverwriteExisting = overwriteExisting;
        _enrichmentState.OverwriteSkip = 0;
        _enrichmentState.LastMessage = overwriteExisting
            ? "Збагачення (SteamSpy) запущено (перезапис)."
            : "Збагачення (SteamSpy) запущено.";
        _enrichmentState.LastError = null;
    }

    public void StopEnrichmentImport()
    {
        _enrichmentState.IsImporting = false;
        _enrichmentState.LastMessage = "Зупинено вручну.";
    }

    public bool StartPriceSync(int batchSize, DateTime? notSyncedSince = null)
    {
        if (_priceState.IsRunning)
            return false;

        batchSize = Math.Clamp(batchSize, 1, 200);
        _priceState.Cts?.Cancel();
        _priceState.Cts = new CancellationTokenSource();
        var token = _priceState.Cts.Token;

        _priceState.IsRunning = true;

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var gameRepo  = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var priceSync = scope.ServiceProvider.GetRequiredService<SteamSpyPriceSyncService>();

            try
            {
                int total;
                Func<int, int, CancellationToken, Task<List<Domain.Entities.Game>>> getBatch;

                if (notSyncedSince.HasValue)
                {
                    var since = notSyncedSince.Value;
                    total    = await gameRepo.GetGamesNotSyncedSinceCountAsync(since, token);
                    getBatch = (skip, take, ct) => gameRepo.GetGamesNotSyncedSinceBatchAsync(since, skip, take, ct);
                    _logger.LogInformation(
                        "SteamSpy price sync (not-synced-since {Since}): {Total} games",
                        since.ToString("yyyy-MM-dd"), total);
                }
                else
                {
                    total    = await gameRepo.GetTotalGamesCountAsync(token);
                    getBatch = (skip, take, ct) => gameRepo.GetGamesBatchAsync(skip, take, ct);
                }

                _priceState.ResetProgress(total);
                _priceState.LastMessage = notSyncedSince.HasValue
                    ? $"Синхронізація цін (після {notSyncedSince.Value:dd.MM.yyyy}) запущена."
                    : "Синхронізація цін (SteamSpy) запущена.";

                for (var skip = 0; skip < total && !token.IsCancellationRequested; skip += batchSize)
                {
                    var batch = await getBatch(skip, batchSize, token);
                    _priceState.LastBatchSize   = batch.Count;
                    _priceState.ProcessedGames  = Math.Min(skip + batch.Count, total);
                    _priceState.LastMessage     =
                        $"Батч {skip + 1}–{Math.Min(skip + batchSize, total)} з {total}";

                    if (batch.Count > 0)
                        await priceSync.SyncPricesBatchAsync(batch, token);
                }

                if (token.IsCancellationRequested)
                {
                    _priceState.MarkFinished("Синхронізацію зупинено.");
                    _logger.LogInformation("SteamSpy price sync stopped by admin.");
                }
                else
                {
                    _priceState.ProcessedGames = total;
                    _priceState.MarkFinished("Синхронізацію завершено.");
                    _logger.LogInformation("SteamSpy price sync completed.");
                }
            }
            catch (OperationCanceledException)
            {
                _priceState.MarkFinished("Синхронізацію зупинено.");
                _logger.LogInformation("SteamSpy price sync cancelled via token.");
            }
            catch (Exception ex)
            {
                _priceState.LastError = ex.Message;
                _priceState.MarkFinished("Помилка синхронізації.");
                _logger.LogError(ex, "SteamSpy price sync failed.");
            }
        }, token);

        return true;
    }
    public void StopPriceSync()
    {
        _priceState.Cts?.Cancel();
        _priceState.IsRunning = false;
        _priceState.LastMessage = "Зупинка…";
    }

    private static ImportJobStatusDto ToStatus(
        bool isRunning,
        string source,
        DateTime? started,
        DateTime? finished,
        int lastBatch,
        string? message,
        string? error,
        int? processed = null,
        int? total = null)
        => new(isRunning, source, started, finished, lastBatch, message, error, processed, total);
}
