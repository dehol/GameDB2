using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using Hangfire;

namespace GameDB.Application.Services;

public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository         _adminRepo;
    private readonly IBackgroundJobClient     _jobClient;
    private readonly BasicImportOperationState _basicImportState;
    private readonly EnrichmentOperationState _enrichmentState;
    private readonly PriceSyncOperationState  _priceSyncState;

    private static readonly string[] PriceSyncProviders  = ["steam", "gog", "epic"];
    private static readonly string[] EnrichmentProviders = ["steam", "gog", "epic"];

    public AdminService(
        IAdminRepository         adminRepo,
        IBackgroundJobClient     jobClient,
        BasicImportOperationState basicImportState,
        EnrichmentOperationState enrichmentState,
        PriceSyncOperationState  priceSyncState)
    {
        _adminRepo        = adminRepo;
        _jobClient        = jobClient;
        _basicImportState = basicImportState;
        _enrichmentState  = enrichmentState;
        _priceSyncState   = priceSyncState;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var stats = await _adminRepo.GetStatsAsync(ct);
        return new AdminDashboardDto(
            stats,
            ImportJobStatusDto.FromState(_basicImportState, "Базовий імпорт"),
            ImportJobStatusDto.FromState(_enrichmentState,  "Збагачення деталей"),
            ImportJobStatusDto.FromState(_priceSyncState,   "Синхронізація цін"));
    }

    public Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter, int page, int pageSize,
        string? search = null, CancellationToken ct = default)
        => _adminRepo.GetGamesAsync(filter, page, pageSize, search, ct);

    public Task<int> ImportBasicGamesAsync(string? providerSlug = null, CancellationToken ct = default)
    {
        _jobClient.Enqueue<IBasicImportService>(s =>
            s.RunImportJobAsync(providerSlug, CancellationToken.None));

        return Task.FromResult(1);
    }

    public void StopBasicImport() => _basicImportState.RequestStop();

    public void StartEnrichmentImport(string? providerSlug = null, bool overwriteExisting = false)
    {
        var providers = providerSlug != null
            ? [providerSlug]
            : EnrichmentProviders;

        _enrichmentState.ResetStop();
        _enrichmentState.PrepareParallelSync("Збагачення деталей", providers.Length);

        foreach (var slug in providers)
        {
            _jobClient.Enqueue<IGameEnrichmentService>(s =>
                s.EnrichProviderAsync(slug, overwriteExisting, CancellationToken.None));
        }
    }

    public void StopEnrichmentImport() => _enrichmentState.RequestStop();

    public bool StartPriceSync(int batchSize, string? providerSlug = null, DateTime? notSyncedSince = null)
    {
        var providers = providerSlug != null
            ? [providerSlug]
            : PriceSyncProviders;

        _priceSyncState.ResetStop();
        _priceSyncState.PrepareParallelSync("Синхронізація цін", providers.Length);

        foreach (var slug in providers)
        {
            _jobClient.Enqueue<IPriceSyncService>(s =>
                s.SyncProviderAsync(slug, CancellationToken.None));
        }

        return true;
    }

    public void StopPriceSync() => _priceSyncState.RequestStop();
}
