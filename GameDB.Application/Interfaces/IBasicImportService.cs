using GameDB.Application.Interfaces;

namespace GameDB.Application.Interfaces;

public interface IBasicImportService
{
    /// <summary>
    /// Фаза 1: Завантажує список ігор з магазину, зіставляє з існуючим каталогом
    /// через нормалізовану назву та зберігає нові GameExternalId-зв'язки.
    /// </summary>
    /// <returns>Кількість нових або пов'язаних ігор.</returns>
    Task<int> ImportBasicAsync(IStoreProvider provider, CancellationToken ct = default);
}
