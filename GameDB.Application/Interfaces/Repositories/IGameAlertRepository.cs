using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameAlertRepository
{
    Task<GamePriceAlertContextDto> GetPriceContextAsync(int gameId, int? userId, CancellationToken ct = default);
    Task<Alert?> GetActiveAlertAsync(int userId, int gameId, CancellationToken ct = default);
    Task AddAsync(Alert alert, CancellationToken ct = default);
    Task UpdateAsync(Alert alert, CancellationToken ct = default);
    Task DeleteAsync(int alertId, CancellationToken ct = default);
}
