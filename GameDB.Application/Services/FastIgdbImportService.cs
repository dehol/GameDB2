using GameDB.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

// F2.2: now only orchestration — auth, HTTP, mapping moved to dedicated classes
public sealed class FastIgdbImportService(
    IIgdbClient         igdbClient,
    IgdbGameMapper      mapper,
    IGameRepository     games,
    ILookupRepository   lookup,
    ILogger<FastIgdbImportService> logger)
{
    private const int Concurrency = 3;

    public async Task ImportDetailsFastAsync(
        IReadOnlyCollection<int> appIds, CancellationToken ct = default)
    {
        logger.LogInformation("IGDB import started for {Count} games", appIds.Count);

        using var semaphore = new SemaphoreSlim(Concurrency, Concurrency);
        var tasks = appIds.Select(appId => ProcessOneAsync(appId, semaphore, ct));
        await Task.WhenAll(tasks);

        logger.LogInformation("IGDB import completed");
    }

    private async Task ProcessOneAsync(int appId, SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var game = await games.GetBySteamIdAsync(appId, ct);
            if (game is null) return;

            var results = await igdbClient.SearchGamesAsync(game.Name, ct);

            if (results.Count == 0)
            {
                logger.LogDebug("IGDB: game not found {Name}", game.Name);
                game.Description = string.Empty;
                game.UpdatedAt   = DateTime.UtcNow;
                await games.UpdateAsync(game, ct);
                return;
            }

            var dto = results[0];
            mapper.ApplyIgdbData(game, dto);

            if (dto.genres is { Count: > 0 })
            {
                game.Genres.Clear();
                foreach (var g in dto.genres.Where(x => !string.IsNullOrEmpty(x.name)))
                {
                    var genre = await lookup.GetOrCreateGenreAsync(g.name!, ct);
                    game.Genres.Add(genre);
                }
            }

            if (dto.involved_companies is { Count: > 0 })
            {
                var devName = dto.involved_companies
                    .FirstOrDefault(c => c.developer)?.company?.name;
                if (!string.IsNullOrEmpty(devName))
                {
                    var dev = await lookup.GetOrCreateDeveloperAsync(devName, ct);
                    game.DeveloperId = dev.DeveloperId;
                }

                var pubName = dto.involved_companies
                    .FirstOrDefault(c => c.publisher)?.company?.name;
                if (!string.IsNullOrEmpty(pubName))
                {
                    var pub = await lookup.GetOrCreatePublisherAsync(pubName, ct);
                    game.PublisherId = pub.PublisherId;
                }
            }

            await games.UpdateAsync(game, ct);
            logger.LogDebug("IGDB: updated {Name}", game.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IGDB: error for AppId {AppId}", appId);
        }
        finally
        {
            await Task.Delay(500, ct); // rate limit throttle
            semaphore.Release();
        }
    }
}