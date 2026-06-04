using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.DTOs;
using GameDB.Application.DTOs.Store;

namespace GameDB.Application.Interfaces;

public interface IGogClient
{
    /// <summary>
    /// Отримує одну сторінку каталогу ігор GOG.
    /// Повертає null якщо сторінка за межами або помилка.
    /// </summary>
    Task<GogCatalogResponseDto?> GetCatalogPageAsync(string cursor, CancellationToken ct = default);

    /// <summary>
    /// Повна інформація про гру за числовим ID.
    /// </summary>
    Task<GogProductDetailsDto?> GetProductDetailsAsync(string productId, CancellationToken ct = default);
    Task<StorePriceInfo?> GetItemPriceAsync(string itemId, CancellationToken ct = default);
}