using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ICatalogService
{
    Task<CatalogResultDto>  GetCatalogAsync(CatalogFilterDto filter, CancellationToken ct = default);
    Task<CatalogSidebarDto> GetSidebarDataAsync(CancellationToken ct = default);
    Task<GameDetailDto?>    GetGameDetailAsync(int gameId, CancellationToken ct = default);
    Task<List<ShopPriceHistoryDto>> GetPriceHistoryAsync(int gameId, CancellationToken ct = default);
}