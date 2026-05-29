using GameDB.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public sealed class GameEnrichmentService(
    IGameRepository games,
    ISteamSpyClient steamSpy,
    SteamSpyGameMapper steamSpyMapper,
    ILogger<GameEnrichmentService> logger)
{
    public async Task ImportBatchAsync(
        List<int> appIds,
        GameEnrichmentImportState importState,
        CancellationToken ct = default)
    {
        var overwriteExisting = importState.OverwriteExisting;
        var metrics = new ImportBatchMetrics();
        var lookupCache = new SteamSpyLookupCache(games);
        var updatedGames = new List<Domain.Entities.Game>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var appId in appIds)
        {
            if (ct.IsCancellationRequested || !importState.IsImporting)
                break;

            try
            {
                var result = await EnrichSingleGameAsync(appId, overwriteExisting, lookupCache, updatedGames, ct);
                switch (result)
                {
                    case EnrichResult.Success:
                        metrics.SuccessCount++;
                        break;
                    case EnrichResult.Skipped:
                        metrics.SkippedCount++;
                        break;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                metrics.RateLimitCount++;
                metrics.ErrorCount++;
                logger.LogWarning(ex, "SteamSpy 429 при збагаченні AppId {AppId}", appId);
            }
            catch (Exception ex)
            {
                metrics.ErrorCount++;
                logger.LogError(ex, "Помилка збагачення гри Steam AppId {AppId}", appId);
            }
        }

        if (updatedGames.Count > 0)
            await games.UpdateBatchAsync(updatedGames, ct);

        stopwatch.Stop();
        importState.RecordBatch(metrics, stopwatch.Elapsed);
        logger.LogInformation(
            "Enrichment batch done: {Summary} elapsed={Elapsed:mm\\:ss}",
            metrics.ToSummary(), stopwatch.Elapsed);
    }

    private enum EnrichResult { Success, Skipped }

    private async Task<EnrichResult> EnrichSingleGameAsync(
        int appId,
        bool overwriteExisting,
        SteamSpyLookupCache lookupCache,
        List<Domain.Entities.Game> updatedGames,
        CancellationToken ct)
    {
        var game = await games.GetBySteamIdAsync(appId, ct);
        if (game is null)
        {
            logger.LogWarning("SteamSpy enrichment: гру AppId {AppId} не знайдено в БД", appId);
            return EnrichResult.Skipped;
        }

        var spy = await steamSpy.GetAppDetailsAsync(appId, ct);
        if (spy is null || string.IsNullOrWhiteSpace(spy.Name) ||
            string.Equals(spy.Name, "none", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("SteamSpy: немає даних для AppId {AppId}", appId);
            return EnrichResult.Skipped;
        }

        await steamSpyMapper.ApplyAsync(game, spy, lookupCache, overwriteExisting, ct);
        updatedGames.Add(game);
        logger.LogDebug("Збагачено: {Name} (SteamSpy)", game.Name);
        return EnrichResult.Success;
    }
}
