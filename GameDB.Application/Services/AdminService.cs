using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Services.Import;
using Hangfire; 

namespace GameDB.Application.Services;

public sealed class AdminService : IAdminService
{
    private readonly IAdminRepository _adminRepo;
    private readonly IBackgroundJobClient _jobClient; 
    private readonly EnrichmentOperationState _enrichmentState;
    private readonly PriceSyncOperationState _priceSyncState;

    public AdminService(
        IAdminRepository adminRepo, 
        IBackgroundJobClient jobClient, 
        EnrichmentOperationState enrichmentState,
        PriceSyncOperationState priceSyncState) 
    {
        _adminRepo = adminRepo;
        _jobClient = jobClient;
        _enrichmentState = enrichmentState;
        _priceSyncState = priceSyncState;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var stats = await _adminRepo.GetStatsAsync(ct);
        
        return new AdminDashboardDto(
            stats, 
            ImportJobStatusDto.FromState(_enrichmentState, "Збагачення деталей"), 
            ImportJobStatusDto.FromState(_priceSyncState, "Синхронізація цін")
        ); 
    }

    public Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter, int page, int pageSize,
        string? search = null, CancellationToken ct = default)
        => _adminRepo.GetGamesAsync(filter, page, pageSize, search, ct);

    public Task<int> ImportBasicGamesAsync(string? providerSlug = null, CancellationToken ct = default)
    {
        _jobClient.Enqueue<IBasicImportService>(service => 
            service.RunImportJobAsync(providerSlug, CancellationToken.None));
            
        return Task.FromResult(1); 
    }

    public void StartEnrichmentImport(bool overwriteExisting = false)
    {
        _jobClient.Enqueue<IGameEnrichmentService>(service => 
            service.RunEnrichmentJobAsync(null, overwriteExisting, CancellationToken.None));
    }

    public bool StartPriceSync(int batchSize, DateTime? notSyncedSince = null)
    {
        _jobClient.Enqueue<IPriceSyncService>(service => 
            service.RunPriceSyncJobAsync(null, CancellationToken.None));
        return true;
    }

    public void StopEnrichmentImport() => _enrichmentState.RequestStop();
    public void StopPriceSync() => _priceSyncState.RequestStop();
}