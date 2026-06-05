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
    private readonly IBasicImportService _basicImportService;
    private readonly IReadOnlyList<IStoreProvider> _providers;
    private readonly ImportOperationState _importState;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        IAdminRepository adminRepo,
        IBasicImportService basicImportService,
        IEnumerable<IStoreProvider> providers,
        ImportOperationState importState,
        ILogger<AdminService> logger)
    {
        _adminRepo       = adminRepo;
        _basicImportService   = basicImportService;
        _providers       = providers.ToList();
        _importState = importState;
        _logger          = logger;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var stats = await _adminRepo.GetStatsAsync(ct);
        return new AdminDashboardDto(
            stats,
            ToStatus(_importState.IsRunning, "import",
                _importState.StartedAt, _importState.FinishedAt,
                _importState.BatchSize, _importState.LastMessage,
                _importState.LastError),
            ToStatus(_importState.IsRunning, "import",
                _importState.StartedAt, _importState.FinishedAt,
                _importState.BatchSize, _importState.LastMessage,
                _importState.LastError,
                _importState.Processed, _importState.Total));
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
            return await _basicImportService.ImportBasicAsync(providers[0], ct);

        var results = await Task.WhenAll(providers.Select(p => RunSafeAsync(p, ct)));
        return results.Sum();
    }

    private async Task<int> RunSafeAsync(IStoreProvider provider, CancellationToken ct)
    {
        try
        {
            var count = await _basicImportService.ImportBasicAsync(provider, ct);
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
        if (!_importState.TryStart()) return;
        _importState.OverwriteExisting = overwriteExisting;
        _importState.OverwriteSkip     = 0;
        _importState.StartedAt         = DateTime.UtcNow;
        _importState.FinishedAt        = null;
        _importState.LastMessage       = "Збагачення запущено.";
        _importState.LastError         = null;
    }

    public void StopEnrichmentImport()
    {
        _importState.RequestStop();
        _importState.LastMessage = "Зупинено вручну.";
    }

    public bool StartPriceSync(int batchSize, DateTime? notSyncedSince = null)
    {
        if (!_importState.TryStart()) return false;
        _importState.Cts?.Cancel();
        _importState.Cts         = new CancellationTokenSource();
        _importState.StartedAt   = DateTime.UtcNow;
        _importState.FinishedAt  = null;
        _importState.LastMessage = "Синхронізацію цін запущено.";
        _importState.LastError   = null;
        return true;
    }

    public void StopPriceSync()
    {
        _importState.Cts?.Cancel();
        _importState.RequestStop();
        _importState.LastMessage = "Зупинка…";
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
