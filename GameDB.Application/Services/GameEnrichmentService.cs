using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public sealed class GameEnrichmentService(
    IGameRepository games,
    ISteamSpyClient steamSpy,
    IIgdbClient igdbClient,
    SteamSpyGameMapper steamSpyMapper,
    IgdbDescriptionMapper descriptionMapper,
    ILogger<GameEnrichmentService> logger,
    IOptions<SteamSpyImportOptions> options)
{
    private readonly SteamSpyImportOptions _options = options.Value;

    public async Task ImportBatchAsync(
        List<int> appIds,
        GameEnrichmentImportState state,
        CancellationToken ct = default)
    {
        foreach (var appId in appIds)
        {
            if (ct.IsCancellationRequested || !state.IsImporting)
                break;

            try
            {
                await EnrichSingleGameAsync(appId, ct);
                await Task.Delay(_options.DelayBetweenRequestsMs, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Помилка збагачення гри Steam AppId {AppId}", appId);
            }
        }
    }

    private async Task EnrichSingleGameAsync(int appId, CancellationToken ct)
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

        await steamSpyMapper.ApplyAsync(game, spy, games, ct);

        var igdb = await igdbClient.GetBySteamIdAsync(appId, ct);
        if (igdb is null)
        {
            logger.LogInformation("IGDB: пошук за назвою «{Name}» (AppId {AppId})", game.Name, appId);
            var searchResults = await igdbClient.SearchGamesAsync(game.Name, ct);
            igdb = searchResults.FirstOrDefault();
        }

        if (igdb is not null && (!string.IsNullOrWhiteSpace(igdb.summary) || !string.IsNullOrWhiteSpace(igdb.storyline)))
        {
            descriptionMapper.ApplyDescription(game, igdb);
            logger.LogInformation("Збагачено: {Name} (SteamSpy + IGDB опис)", game.Name);
        }
        else
        {
            logger.LogWarning("IGDB: опис не знайдено для {Name} (AppId {AppId})", game.Name, appId);
        }

        await games.UpdateAsync(game, ct);
    }
}
