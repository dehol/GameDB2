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

    Task<int> ImportBasicGamesAsync(CancellationToken ct = default);
    void StartDetailsImport(string source);
    void StopDetailsImport();
    bool StartPriceSync(int batchSize);
    void StopPriceSync();
}
