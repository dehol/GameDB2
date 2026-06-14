using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameAlertRepository
{
    Task<GamePriceAlertContextDto> GetPriceContextAsync(int gameId, int? userId, CancellationToken ct = default);

    /// <summary>Повертає всі алерти користувача (активні та спрацьовані) з включеними Game.GameExternalIds.GameOffers.</summary>
    Task<List<Alert>> GetByUserIdAsync(int userId, CancellationToken ct = default);

    Task<Alert?> GetActiveAlertAsync(int userId, int gameId, CancellationToken ct = default);

    /// <summary>Повертає всі алерти, що ще не спрацювали, з включеними даними гри та офферами для перевірки цін.</summary>
    Task<List<Alert>> GetActiveAlertsAsync(CancellationToken ct = default);

    Task AddAsync(Alert alert, CancellationToken ct = default);
    Task UpdateAsync(Alert alert, CancellationToken ct = default);
    Task DeleteAsync(int alertId, CancellationToken ct = default);
}
