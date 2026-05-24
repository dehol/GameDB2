using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IAlertRepository
{
    Task<List<Alert>> GetByUserIdAsync(int userId);
    Task<Alert?> GetByIdAsync(int alertId);
    Task AddAsync(Alert alert);
    Task UpdateAsync(Alert alert);
    Task DeleteAsync(int alertId);
    Task<List<Alert>> GetActiveAlertsAsync();
}
