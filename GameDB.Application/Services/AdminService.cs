using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository _adminRepo;
    private readonly StoreImportService _importService;
    private readonly IReadOnlyList<IStoreProvider> _providers;
    private readonly EnrichmentOperationState _enrichmentState;
    private readonly PriceSyncOperationState _priceState;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IAdminRepository adminRepo,
        StoreImportService importService,
        IEnumerable<IStoreProvider> providers,
        EnrichmentOperationState enrichmentState,
        PriceSyncOperationState priceState,
        ILogger<AdminService> logger)
    {
        _adminRepo       = adminRepo;
        _importService   = importService;
        _providers       = providers.ToList();
        _enrichmentState = enrichmentState;
        _priceState      = priceState;
        _logger          = logger;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var stats = await _adminRepo.GetStatsAsync(ct);
        return new AdminDashboardDto(
            stats,
            ToStatus(_enrichmentState.IsRunning, "enrichment",
                _enrichmentState.StartedAt, _enrichmentState.FinishedAt,
                _enrichmentState.BatchSize, _enrichmentState.LastMessage,
                _enrichmentState.LastError),
            ToStatus(_priceState.IsRunning, "price_sync",
                _priceState.StartedAt, _priceState.FinishedAt,
                _priceState.BatchSize, _priceState.LastMessage,
                _priceState.LastError,
                _priceState.Processed, _priceState.Total));
    }

    public Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter, int page, int pageSize,
        string? search = null, CancellationToken ct = default)
        => _adminRepo.GetGamesAsync(filter, page, pageSize, search, ct);

    /// <summary>
    /// FIX: Паралельний запуск всіх провайдерів через Task.WhenAll.
    /// Помилка одного не скасовує інші — кожен ізольований у RunSafeAsync.
    /// </summary>
    public async Task<int> ImportBasicGamesAsync(
        string? providerSlug = null, CancellationToken ct = default)
    {
        var providers = ResolveProviders(providerSlug);

        if (providers.Count == 1)
            return await _importService.ImportBasicAsync(providers[0], ct);

        var results = await Task.WhenAll(providers.Select(p => RunSafeAsync(p, ct)));
        return results.Sum();
    }

    private async Task<int> RunSafeAsync(IStoreProvider provider, CancellationToken ct)
    {
        try
        {
            var count = await _importService.ImportBasicAsync(provider, ct);
            _logger.LogInformation("[{Slug}] Basic import завершено: {Count} ігор", provider.Slug, count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Slug}] Basic import завершився з помилкою", provider.Slug);
            return 0;
        }
    }

    public void StartEnrichmentImport(bool overwriteExisting = false)
    {
        if (_enrichmentState.IsRunning) return;
        _enrichmentState.IsRunning         = true;
        _enrichmentState.OverwriteExisting = overwriteExisting;
        _enrichmentState.OverwriteSkip     = 0;
        _enrichmentState.StartedAt         = DateTime.UtcNow;
        _enrichmentState.FinishedAt        = null;
        _enrichmentState.LastMessage       = "Збагачення запущено.";
        _enrichmentState.LastError         = null;
    }

    public void StopEnrichmentImport()
    {
        _enrichmentState.IsRunning   = false;
        _enrichmentState.LastMessage = "Зупинено вручну.";
    }

    public bool StartPriceSync(int batchSize, DateTime? notSyncedSince = null)
    {
        if (_priceState.IsRunning) return false;
        _priceState.Cts?.Cancel();
        _priceState.Cts         = new CancellationTokenSource();
        _priceState.IsRunning   = true;
        _priceState.StartedAt   = DateTime.UtcNow;
        _priceState.FinishedAt  = null;
        _priceState.LastMessage = "Синхронізацію цін запущено.";
        _priceState.LastError   = null;
        return true;
    }

    public void StopPriceSync()
    {
        _priceState.Cts?.Cancel();
        _priceState.IsRunning   = false;
        _priceState.LastMessage = "Зупинка…";
    }

    private IReadOnlyList<IStoreProvider> ResolveProviders(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return _providers;
        var p = _providers.FirstOrDefault(x =>
            string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (p is null)
            throw new ArgumentException($"Невідомий провайдер: {slug}", nameof(slug));
        return [p];
    }

    private static ImportJobStatusDto ToStatus(
        bool isRunning, string source, DateTime? started, DateTime? finished,
        int lastBatch, string? message, string? error,
        int? processed = null, int? total = null)
        => new(isRunning, source, started, finished, lastBatch, message, error, processed, total);
}
