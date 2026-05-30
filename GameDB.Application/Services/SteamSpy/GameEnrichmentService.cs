using GameDB.Application.Interfaces;
using GameDB.Application.Options;
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
        List<int> appIds,
        GameEnrichmentImportState state,
        CancellationToken ct = default)
    {
        var overwriteExisting = state.OverwriteExisting;
        foreach (var appId in appIds)
        {
            if (ct.IsCancellationRequested || !state.IsImporting)
                break;

            try
            {
                await EnrichSingleGameAsync(appId, overwriteExisting, ct);
                await Task.Delay(_options.DelayBetweenRequestsMs, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Помилка збагачення гри Steam AppId {AppId}", appId);
            }
        }
    }

    private async Task EnrichSingleGameAsync(int appId, bool overwriteExisting, CancellationToken ct)
    {
        var game = await games.GetBySteamIdAsync(appId, ct);
        if (game is null) return;

        var spy = await steamSpy.GetAppDetailsAsync(appId, ct);
        if (spy is null || string.IsNullOrWhiteSpace(spy.Name) ||
            string.Equals(spy.Name, "none", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("SteamSpy: немає даних для AppId {AppId}", appId);
            return;
        }

        await steamSpyMapper.ApplyAsync(game, spy, games, overwriteExisting, ct);

        logger.LogInformation("Збагачено: {Name} (SteamSpy)", game.Name);

        await games.UpdateAsync(game, ct);
    }
}
