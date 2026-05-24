using GameDB.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using GameDB.Domain.Entities;

namespace GameDB.Infrastructure.Data.Repositories;

public class AlertRepository : IAlertRepository
{
    private readonly AppDbContext _db;

    public AlertRepository(AppDbContext db) => _db = db;

    public async Task<List<Alert>> GetByUserIdAsync(int userId)
        => await _db.Alerts
            .Where(a => a.UserId == userId)
            .Include(a => a.Game)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

    public async Task<Alert?> GetByIdAsync(int alertId)
        => await _db.Alerts.FindAsync(alertId);

    public async Task AddAsync(Alert alert)
    {
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Alert alert)
    {
        _db.Alerts.Update(alert);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int alertId)
    {
        await _db.Alerts
            .Where(a => a.AlertId == alertId)
            .ExecuteDeleteAsync();
    }

    public async Task<List<Alert>> GetActiveAlertsAsync()
        => await _db.Alerts
            .Where(a => a.TriggeredAt == null)
            .Include(a => a.Game)
                .ThenInclude(g => g.GameOffers)
            .ToListAsync();
}
