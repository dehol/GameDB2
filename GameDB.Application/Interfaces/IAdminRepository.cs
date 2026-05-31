using GameDB.Application.DTOs;
using GameDB.Domain.Enums;
namespace GameDB.Application.Interfaces;

public interface IAdminRepository
{
    Task<AdminStatsDto> GetStatsAsync(CancellationToken ct = default);
    Task<int> CountByStatusAsync(GameImportStatus status, CancellationToken ct = default);
    Task<AdminGameListDto> GetGamesAsync(
        AdminGameCoverageFilter filter,
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default);
}
