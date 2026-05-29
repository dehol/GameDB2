using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IGameAlertService
{
    Task<GamePriceAlertContextDto> GetPriceContextAsync(int gameId, int? userId = null, CancellationToken ct = default);
    Task SaveAlertAsync(int userId, SavePriceAlertDto dto, CancellationToken ct = default);
    Task DeleteAlertAsync(int userId, int gameId, CancellationToken ct = default);
}
