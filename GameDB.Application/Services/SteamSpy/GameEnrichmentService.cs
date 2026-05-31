using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Application.Services;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public sealed class GameEnrichmentService(
    IGameRepository games,
    ISteamSpyClient steamSpy,
    SteamSpyGameMapper steamSpyMapper,
    ILogger<GameEnrichmentService> logger,
    IOptions<SteamSpyImportOptions> options)
{
    private readonly SteamSpyImportOptions _options = options.Value;

    public async Task ImportBatchAsync(
        List<string> externalIds,
        GameEnrichmentImportState state,
        CancellationToken ct = default)
    {
        foreach (var externalId in externalIds)
        {
            if (ct.IsCancellationRequested || !state.IsImporting) break;

            // Парсинг Steam AppId — локально в сервісі, не в репозиторії
            if (!int.TryParse(externalId, out var appId)) continue;

            try
            {
                await EnrichSingleGameAsync(appId, externalId, state.OverwriteExisting, ct);
                await Task.Delay(_options.DelayBetweenRequestsMs, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Помилка збагачення гри ExternalId={ExternalId}", externalId);
            }
        }
    }

    private async Task EnrichSingleGameAsync(
        int appId, string externalId, bool overwriteExisting, CancellationToken ct)
    {
        var game = await games.GetByExternalIdAsync(SteamSpyImportService.SteamShopId, externalId, ct);
        if (game is null) return;

        var spy = await steamSpy.GetAppDetailsAsync(appId, ct);
        if (spy is null
            || string.IsNullOrWhiteSpace(spy.Name)
            || string.Equals(spy.Name, "none", StringComparison.OrdinalIgnoreCase))
        {
            game.ImportStatus = GameImportStatus.Fail;
            logger.LogWarning("SteamSpy: немає даних для AppId {AppId}", appId);
            await games.UpdateAsync(game, ct);
            return;
        }

        await steamSpyMapper.ApplyAsync(game, spy, games, overwriteExisting, ct);

        game.ImportStatus = GameImportStatus.Full;

        logger.LogInformation("Збагачено: {Name} (SteamSpy)", game.Name);

        await games.UpdateAsync(game, ct);
    }
}