using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IGogClient
{
    /// <summary>
    /// Отримує одну сторінку каталогу ігор GOG.
    /// Повертає null якщо сторінка за межами або помилка.
    /// </summary>
    Task<GogFilteredResponseDto?> GetGamesPageAsync(int page, CancellationToken ct = default);

    /// <summary>
    /// Повна інформація про гру за числовим ID.
    /// </summary>
    Task<GogProductDetailsDto?> GetProductDetailsAsync(string productId, CancellationToken ct = default);
}