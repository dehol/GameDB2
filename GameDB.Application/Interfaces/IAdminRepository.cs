using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IAdminRepository
{
    Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<int> CountWithoutDetailsAsync(CancellationToken ct = default);
    Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter,
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default);
}
