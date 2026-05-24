using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface ICatalogService
{
    Task<(List<GameSummaryDto> items, int totalCount)> GetCatalogAsync();
}
