using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

public sealed class UserCollectionService(
    IUserCollectionRepository collections,
    IUserRepository users,
    IGameRepository games,
    IGameShopRepository shops,
    IAlertRepository alerts,
    ISteamClient steam) : IUserCollectionService
{
    public Task<List<UserGameListItemDto>> GetWishlistAsync(int userId, CancellationToken ct = default)
        => collections.GetWishlistAsync(userId, ct);

    public async Task AddToWishlistAsync(int userId, int gameId, CancellationToken ct = default)
    {
        if (await games.GetByIdAsync(gameId, ct) is null)
            throw new InvalidOperationException("Гру не знайдено.");
        await collections.AddToWishlistAsync(userId, gameId, ct);
    }

    public Task RemoveFromWishlistAsync(int userId, int gameId, CancellationToken ct = default)
        => collections.RemoveFromWishlistAsync(userId, gameId, ct);

    public async Task<ImportResultDto> ImportSteamWishlistAsync(int userId, CancellationToken ct = default)
    {
        var steamId = await RequireSteamIdAsync(userId, ct);
        if (steamId.Error is not null) return steamId.ErrorResult!;

        var appIds = await steam.GetWishlistAppIdsAsync(steamId.SteamId!, ct);
        if (appIds.Count == 0)
            return ImportResultDto.Fail(
                "Не вдалося отримати wishlist Steam. Перевірте API-ключ, приватність профілю та прив'язку Steam.");

        var gameIds = await collections.MapSteamAppIdsToGameIdsAsync(appIds, ct);
        var added   = await collections.AddWishlistBulkAsync(userId, gameIds, ct);
        return ImportResultDto.Ok(added, appIds.Count - gameIds.Count, appIds.Count);
    }

    public Task<List<UserLibraryItemDto>> GetLibraryAsync(int userId, CancellationToken ct = default)
        => collections.GetLibraryAsync(userId, ct);

    public async Task AddToLibraryAsync(int userId, int gameId, int? shopId, CancellationToken ct = default)
    {
        if (await games.GetByIdAsync(gameId, ct) is null)
            throw new InvalidOperationException("Гру не знайдено.");

        var sid = shopId ?? await shops.GetSteamShopIdAsync(ct)
            ?? throw new InvalidOperationException("Магазин Steam не знайдено в базі.");

        await collections.AddToLibraryAsync(userId, gameId, sid, ct);
    }

    public Task RemoveFromLibraryAsync(int userId, int gameId, int shopId, CancellationToken ct = default)
        => collections.RemoveFromLibraryAsync(userId, gameId, shopId, ct);

    public async Task<ImportResultDto> ImportSteamLibraryAsync(int userId, CancellationToken ct = default)
    {
        var steamId = await RequireSteamIdAsync(userId, ct);
        if (steamId.Error is not null) return steamId.ErrorResult!;

        var shopId = await shops.GetSteamShopIdAsync(ct);
        if (shopId is null)
            return ImportResultDto.Fail("Магазин Steam не знайдено в базі.");

        var appIds = await steam.GetOwnedGameAppIdsAsync(steamId.SteamId!, ct);
        if (appIds.Count == 0)
            return ImportResultDto.Fail(
                "Не вдалося отримати бібліотеку Steam. Перевірте API-ключ і що профіль/бібліотека публічні.");

        var gameIds = await collections.MapSteamAppIdsToGameIdsAsync(appIds, ct);
        var added   = await collections.AddLibraryBulkAsync(userId, gameIds, shopId.Value, ct);
        return ImportResultDto.Ok(added, appIds.Count - gameIds.Count, appIds.Count);
    }

    public async Task<List<AlertListItemDto>> GetAlertsAsync(int userId, CancellationToken ct = default)
    {
        var list = await alerts.GetByUserIdAsync(userId);
        return list.Select(a =>
        {
            var best = a.Game.GameOffers.OrderBy(o => o.FinalPrice ?? o.CurrentPrice).FirstOrDefault();
            return new AlertListItemDto(
                a.AlertId,
                a.GameId,
                a.Game.Name,
                a.Game.HeaderImage,
                a.TargetPrice,
                best?.FinalPrice,
                best?.Currency,
                a.CreatedAt,
                a.TriggeredAt,
                a.TriggeredAt is null);
        }).ToList();
    }

    public async Task CreateAlertAsync(int userId, CreateAlertDto dto, CancellationToken ct = default)
    {
        if (dto.TargetPrice <= 0)
            throw new InvalidOperationException("Цільова ціна має бути більше нуля.");

        if (await games.GetByIdAsync(dto.GameId, ct) is null)
            throw new InvalidOperationException("Гру не знайдено.");

        var duplicate = (await alerts.GetByUserIdAsync(userId))
            .Any(a => a.GameId == dto.GameId && a.TriggeredAt is null && a.TargetPrice == dto.TargetPrice);

        if (duplicate)
            throw new InvalidOperationException("Такий активний алерт вже існує.");

        await alerts.AddAsync(new Alert
        {
            UserId      = userId,
            GameId      = dto.GameId,
            TargetPrice = dto.TargetPrice,
            CreatedAt   = DateTime.UtcNow,
        });
    }

    public async Task DeleteAlertAsync(int userId, int alertId, CancellationToken ct = default)
    {
        var alert = await alerts.GetByIdAsync(alertId);
        if (alert is null || alert.UserId != userId)
            throw new InvalidOperationException("Алерт не знайдено.");
        await alerts.DeleteAsync(alertId);
    }

    public Task<GameCollectionStateDto> GetCollectionStateAsync(int userId, int gameId, CancellationToken ct = default)
        => collections.GetCollectionStateAsync(userId, gameId, ct);

    private async Task<(string? SteamId, ImportResultDto? ErrorResult, string? Error)> RequireSteamIdAsync(
        int userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId);
        if (user is null)
            return (null, ImportResultDto.Fail("Користувача не знайдено."), "user");

        if (string.IsNullOrEmpty(user.SteamId))
            return (null, ImportResultDto.Fail("Спочатку прив'яжіть Steam у профілі."), "steam");

        return (user.SteamId, null, null);
    }
}
