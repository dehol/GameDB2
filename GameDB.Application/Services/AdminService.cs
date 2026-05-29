using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository _adminRepo;
    private readonly IGameRepository _gameRepo;
    private readonly SteamImportService _steamImport;
    private readonly GameEnrichmentImportState _enrichmentState;
    private readonly PriceSyncState _priceState;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IAdminRepository adminRepo,
        IGameRepository gameRepo,
        SteamImportService steamImport,
        GameEnrichmentImportState enrichmentState,
        PriceSyncState priceState,
        IServiceScopeFactory scopeFactory,
        ILogger<AdminService> logger)
    {
        _adminRepo = adminRepo;
        _gameRepo = gameRepo;
        _steamImport = steamImport;
        _enrichmentState = enrichmentState;
        _priceState = priceState;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var stats = await _adminRepo.GetStatsAsync(ct);
        var pending = await _adminRepo.CountWithoutDetailsAsync(ct);

        return new AdminDashboardDto(
            stats,
            ToEnrichmentStatus(),
            ToPriceStatus(),
            pending);
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
        _enrichmentState.ResetCounters();
        _enrichmentState.LastMessage = overwriteExisting
            ? "Збагачення SteamSpy запущено (перезапис)."
            : "Збагачення SteamSpy запущено.";
        _enrichmentState.LastError = null;
    }

    public void StopEnrichmentImport()
    {
        _enrichmentState.IsImporting = false;
        _enrichmentState.LastMessage = "Зупинено вручну.";
    }

    public bool StartPriceSync(int batchSize)
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
            var gameRepo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var priceSync = scope.ServiceProvider.GetRequiredService<SteamSpyPriceSyncService>();

            try
            {
                var total = await gameRepo.GetTotalGamesCountAsync(token);
                _priceState.ResetProgress(total);
                _priceState.LastMessage = "Синхронізація цін (SteamSpy) запущена.";

                for (var skip = 0; skip < total && !token.IsCancellationRequested; skip += batchSize)
                {
                    var batch = await gameRepo.GetGamesBatchAsync(skip, batchSize, token);
                    _priceState.LastBatchSize = batch.Count;
                    _priceState.ProcessedGames = Math.Min(skip + batch.Count, total);
                    _priceState.LastMessage =
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
                    _logger.LogInformation("SteamSpy price sync completed. {Summary}",
                        _priceState.LastBatchSummary ?? "OK");
                }
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

    private ImportJobStatusDto ToEnrichmentStatus()
        => new(
            _enrichmentState.IsImporting,
            "enrichment",
            _enrichmentState.StartedAt,
            _enrichmentState.FinishedAt,
            _enrichmentState.LastBatchSize,
            _enrichmentState.LastMessage,
            _enrichmentState.LastError,
            SuccessCount: _enrichmentState.SuccessCount,
            ErrorCount: _enrichmentState.ErrorCount,
            SkippedCount: _enrichmentState.SkippedCount,
            RateLimitCount: _enrichmentState.RateLimitCount,
            LastBatchSummary: _enrichmentState.LastBatchSummary,
            LastWarning: _enrichmentState.LastWarning);

    private ImportJobStatusDto ToPriceStatus()
        => new(
            _priceState.IsRunning,
            "steamspy",
            _priceState.StartedAt,
            _priceState.FinishedAt,
            _priceState.LastBatchSize,
            _priceState.LastMessage,
            _priceState.LastError,
            _priceState.ProcessedGames,
            _priceState.TotalGames,
            _priceState.SuccessCount,
            _priceState.ErrorCount,
            _priceState.SkippedCount,
            _priceState.RateLimitCount,
            _priceState.LastBatchSummary,
            _priceState.LastWarning);
}
