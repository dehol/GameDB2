using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

public sealed class GameAlertService(
    IGameAlertRepository alertRepo,
    IGameRepository games) : IGameAlertService
{
    public Task<GamePriceAlertContextDto> GetPriceContextAsync(
        int gameId, int? userId = null, CancellationToken ct = default)
        => alertRepo.GetPriceContextAsync(gameId, userId, ct);

    public async Task SaveAlertAsync(int userId, SavePriceAlertDto dto, CancellationToken ct = default)
    {
        if (dto.TargetPrice <= 0)
            throw new InvalidOperationException("Цільова ціна має бути більше нуля.");

        if (await games.GetByIdAsync(dto.GameId, ct) is null)
            throw new InvalidOperationException("Гру не знайдено.");

        var ctx = await alertRepo.GetPriceContextAsync(dto.GameId, userId, ct);

        if (dto.ShopId.HasValue && ctx.Shops.All(s => s.ShopId != dto.ShopId.Value))
            throw new InvalidOperationException("Обраний магазин недоступний для цієї гри.");

        var existing = await alertRepo.GetActiveAlertAsync(userId, dto.GameId, ct);
        var mode     = dto.AutoUpdateMode.ToString();

        if (existing is not null)
        {
            existing.TargetPrice     = dto.TargetPrice;
            existing.AutoUpdate      = dto.AutoUpdate;
            existing.AutoUpdateMode  = mode;
            existing.ShopId          = dto.ShopId;
            existing.ReferenceLowest = ctx.CurrentLowest;
            existing.TriggeredAt     = null;
            await alertRepo.UpdateAsync(existing, ct);
            return;
        }

        await alertRepo.AddAsync(new Alert
        {
            UserId          = userId,
            GameId          = dto.GameId,
            TargetPrice     = dto.TargetPrice,
            AutoUpdate      = dto.AutoUpdate,
            AutoUpdateMode  = mode,
            ShopId          = dto.ShopId,
            ReferenceLowest = ctx.CurrentLowest,
            CreatedAt       = DateTime.UtcNow,
        }, ct);
    }

    public async Task DeleteAlertAsync(int userId, int gameId, CancellationToken ct = default)
    {
        var alert = await alertRepo.GetActiveAlertAsync(userId, gameId, ct);
        if (alert is null)
            throw new InvalidOperationException("Активний алерт не знайдено.");
        await alertRepo.DeleteAsync(alert.AlertId, ct);
    }
}
