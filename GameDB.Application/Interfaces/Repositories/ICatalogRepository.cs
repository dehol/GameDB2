using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

/// <summary>
/// Read-only репозиторій для каталогу ігор.
/// Повертає готові проекції (DTO) — без зворотного запису.
/// Реалізація в Infrastructure (AppDbContext).
/// </summary>
public interface ICatalogRepository
{
    Task<List<CatalogGameDto>> GetPagedAsync(
        CatalogFilterDto filter, CancellationToken ct = default);

    Task<CatalogSidebarDto> GetSidebarDataAsync(CancellationToken ct = default);

    Task<GameDetailDto?> GetDetailAsync(int gameId, CancellationToken ct = default);

    Task<List<ShopPriceHistoryDto>> GetPriceHistoryAsync(int gameId, CancellationToken ct = default);
    Task<int> GetCountAsync(CatalogFilterDto f, CancellationToken ct = default);
}