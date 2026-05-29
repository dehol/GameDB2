using System.Collections.Concurrent;
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
        GameEnrichmentImportState importState,
        SteamSpyLookupCache lookupCache,
        CancellationToken ct = default)
    {
        var overwriteExisting = importState.OverwriteExisting;
        var metrics = new ImportBatchMetrics();
        var updatedGames = new ConcurrentBag<Domain.Entities.Game>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var gamesBySteamId = await games.GetBySteamIdsAsync(appIds, ct);
        var concurrency = Math.Max(1, _options.EnrichmentConcurrency);
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);

        var tasks = appIds.Select(appId => ProcessAppAsync(
            appId,
            overwriteExisting,
            importState,
            lookupCache,
            gamesBySteamId,
            updatedGames,
            metrics,
            semaphore,
            ct));

        await Task.WhenAll(tasks);

        if (updatedGames.Count > 0)
            await games.UpdateBatchAsync(updatedGames.ToList(), ct);

        stopwatch.Stop();
        importState.RecordBatch(metrics, stopwatch.Elapsed);
        logger.LogInformation(
            "Enrichment batch done: {Summary} elapsed={Elapsed:mm\\:ss}",
            metrics.ToSummary(), stopwatch.Elapsed);
    }

    private async Task ProcessAppAsync(
        int appId,
        bool overwriteExisting,
        GameEnrichmentImportState importState,
        SteamSpyLookupCache lookupCache,
        Dictionary<int, Domain.Entities.Game> gamesBySteamId,
        ConcurrentBag<Domain.Entities.Game> updatedGames,
        ImportBatchMetrics metrics,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !importState.IsImporting)
            return;

        await semaphore.WaitAsync(ct);
        try
        {
            if (!gamesBySteamId.TryGetValue(appId, out var game))
            {
                lock (metrics) { metrics.SkippedCount++; }
                logger.LogWarning("SteamSpy enrichment: гру AppId {AppId} не знайдено в БД", appId);
                return;
            }

            var spy = await steamSpy.GetAppDetailsAsync(appId, ct);
            if (spy is null || string.IsNullOrWhiteSpace(spy.Name) ||
                string.Equals(spy.Name, "none", StringComparison.OrdinalIgnoreCase))
            {
                lock (metrics) { metrics.SkippedCount++; }
                logger.LogWarning("SteamSpy: немає даних для AppId {AppId}", appId);
                return;
            }

            await steamSpyMapper.ApplyAsync(game, spy, lookupCache, overwriteExisting, ct);
            updatedGames.Add(game);
            lock (metrics) { metrics.SuccessCount++; }
            logger.LogDebug("Збагачено: {Name} (SteamSpy)", game.Name);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            lock (metrics)
            {
                metrics.RateLimitCount++;
                metrics.ErrorCount++;
            }
            logger.LogWarning(ex, "SteamSpy 429 при збагаченні AppId {AppId}", appId);
        }
        catch (Exception ex)
        {
            lock (metrics) { metrics.ErrorCount++; }
            logger.LogError(ex, "Помилка збагачення гри Steam AppId {AppId}", appId);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
