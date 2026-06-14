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

    // Slug'и повинні збігатися з IStoreProvider.Slug у кожному провайдері
    private static readonly string[] PriceSyncProviders  = ["steam", "gog", "epic"];
    private static readonly string[] EnrichmentProviders = ["steam", "gog", "epic"];

    public AdminService(
        IAdminRepository         adminRepo,
        IBackgroundJobClient     jobClient,
        BasicImportOperationState basicImportState,
        EnrichmentOperationState enrichmentState,
        PriceSyncOperationState  priceSyncState)
    {
        _adminRepo       = adminRepo;
        _jobClient       = jobClient;
        _basicImportState = basicImportState;
        _enrichmentState = enrichmentState;
        _priceSyncState  = priceSyncState;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var stats = await _adminRepo.GetStatsAsync(ct);
        return new AdminDashboardDto(
            stats,
            ImportJobStatusDto.FromState(_enrichmentState, "Збагачення деталей"),
            ImportJobStatusDto.FromState(_priceSyncState,  "Синхронізація цін"),
            ImportJobStatusDto.FromState(_basicImportState, "Базовий імпорт"));
    }

    public Task<AdminGameListDto> GetGamesAsync(AdminGameCoverageFilter filter, int page, int pageSize,string? search = null, CancellationToken ct = default)
        => _adminRepo.GetGamesAsync(filter, page, pageSize, search, ct);

    public Task<int> ImportBasicGamesAsync(string? providerSlug = null, CancellationToken ct = default)
    {
        _jobClient.Enqueue<IBasicImportService>(s =>
            s.RunImportJobAsync(providerSlug, CancellationToken.None));

        return Task.FromResult(1);
    }

    public void StartEnrichmentImport(bool overwriteExisting = false)
    {
        _enrichmentState.ResetStop();
        _enrichmentState.PrepareParallelSync("Збагачення деталей", EnrichmentProviders.Length);

        foreach (var slug in EnrichmentProviders)
        {
            _jobClient.Enqueue<IGameEnrichmentService>(s =>
                s.EnrichProviderAsync(slug, overwriteExisting, CancellationToken.None));
        }
    }

    /// <summary>
    /// Запускає по одному Hangfire-job на кожен провайдер — паралельно.
    /// IsRunning = true виставляється одразу → UI показує прогрес без затримки.
    /// StopToken лінкується в SyncProviderAsync → Stop перериває HTTP-запити.
    /// </summary>
    /// <param name="batchSize">Зарезервовано. Наразі batch size фіксований у PriceSyncService.</param>
    /// <param name="notSyncedSince">Зарезервовано. Фільтрація ще не підключена до sync-пайплайну.</param>
    public bool StartPriceSync(int batchSize, DateTime? notSyncedSince = null)
    {
        _priceSyncState.ResetStop();
        _priceSyncState.PrepareParallelSync("Синхронізація цін", PriceSyncProviders.Length);

        foreach (var slug in PriceSyncProviders)
        {
            // CancellationToken.None — Hangfire замінює на ShutdownToken при виконанні
            _jobClient.Enqueue<IPriceSyncService>(s =>
                s.SyncProviderAsync(slug, CancellationToken.None));
        }

        return true;
    }

    public void StopEnrichmentImport() => _enrichmentState.RequestStop();

    // RequestStop() скасовує StopToken → переривається через LinkedCts у SyncProviderAsync
    public void StopPriceSync() => _priceSyncState.RequestStop();
}
