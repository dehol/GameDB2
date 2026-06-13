using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;

namespace GameDB.Application.Services;

public class CatalogService : ICatalogService
{
    private readonly ICatalogRepository _repo;

    public CatalogService(ICatalogRepository repo) => _repo = repo;

    public async Task<CatalogResultDto> GetCatalogAsync(CatalogFilterDto filter, CancellationToken ct = default)
    {
        // 1. Отримуємо тільки елементи поточної сторінки
        var items = await _repo.GetPagedAsync(filter, ct);

        // 2. Рахуємо total count окремим швидким запитом
        var total = await _repo.GetCountAsync(filter, ct);

        return new CatalogResultDto(
            Items:      items,
            TotalCount: total,
            Page:       filter.Page,
            PageSize:   filter.PageSize,
            TotalPages: (int)Math.Ceiling(total / (double)filter.PageSize)
        );
    }

    public Task<CatalogSidebarDto> GetSidebarDataAsync(CancellationToken ct = default)
        => _repo.GetSidebarDataAsync(ct);

    public Task<GameDetailDto?> GetGameDetailAsync(int gameId, CancellationToken ct = default)
        => _repo.GetDetailAsync(gameId, ct);

    public Task<List<ShopPriceHistoryDto>> GetPriceHistoryAsync(int gameId, CancellationToken ct = default)
        => _repo.GetPriceHistoryAsync(gameId, ct);
}