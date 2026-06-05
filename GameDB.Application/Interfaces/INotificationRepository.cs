using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface INotificationRepository
{
    /// <summary>Додає нотифікацію до поточного Unit of Work (без збереження).</summary>
    void Add(Notification notification);

    /// <summary>Зберігає всі відкладені зміни в БД.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
