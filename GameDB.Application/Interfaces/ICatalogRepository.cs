using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

/// <summary>
/// Read-only репозиторій для каталогу ігор.
/// Повертає готові проекції (DTO) — без зворотного запису.
/// Реалізація в Infrastructure (AppDbContext).
/// </summary>
public interface ICatalogRepository
{
    Task<(List<CatalogGameDto> Items, int TotalCount)> GetPagedAsync(
        CatalogFilterDto filter, CancellationToken ct = default);

    Task<CatalogSidebarDto> GetSidebarDataAsync(CancellationToken ct = default);

    Task<GameDetailDto?> GetDetailAsync(int gameId, CancellationToken ct = default);
}