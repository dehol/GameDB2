using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IAdminService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter,
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default);

    Task<int> ImportBasicGamesAsync(string? providerSlug = null, CancellationToken ct = default);
    void StopBasicImport();
    void StartEnrichmentImport(string? providerSlug = null, bool overwriteExisting = false);
    void StopEnrichmentImport();
    bool StartPriceSync(int batchSize, string? providerSlug = null, DateTime? notSyncedSince = null);
    void StopPriceSync();
}
