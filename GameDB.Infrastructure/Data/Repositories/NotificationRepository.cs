using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Infrastructure.Data.Repositories;

/// <summary>
/// Репозиторій нотифікацій — замінює пряме використання AppDbContext
/// в AlertCheckerHostedService. Зареєстрований як Scoped.
/// </summary>
public sealed class NotificationRepository(AppDbContext db) : INotificationRepository
{
    /// <inheritdoc />
    public void Add(Notification notification)
        => db.Notifications.Add(notification);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
