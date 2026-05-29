using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public class SteamImportService
{
    private readonly IGameRepository _games;
    private readonly ISteamClient _steamClient;
    private readonly ISteamSpyClient _steamSpy;
    private readonly SteamGameFilter _filter;
    private readonly GameMapper _mapper;
    private readonly ILogger<SteamImportService> _logger;
    private readonly SteamImportOptions _options;

    public SteamImportService(
        IGameRepository games,
        ISteamClient steamClient,
        ISteamSpyClient steamSpy,
        SteamGameFilter filter,
        GameMapper mapper,
        ILogger<SteamImportService> logger,
        IOptions<SteamImportOptions> options)
    {
        _games = games;
        _steamClient = steamClient;
        _steamSpy = steamSpy;
        _filter = filter;
        _mapper = mapper;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<int> ImportBasicGamesAsync(CancellationToken ct = default)
    {
        IReadOnlyCollection<SteamSpyAppListItemDto> steamSpyGames;
        try
        {
            steamSpyGames = await _steamSpy.GetAllAppsAsync(ct);
            _logger.LogInformation("SteamSpy basic import: завантажено {Count} appid з API", steamSpyGames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SteamSpy app list import failed");
            return 0;
        }

        var existingIds = await _games.GetExistingSteamAppIdsAsync(ct);

        var appMap = new Dictionary<int, string>();
        foreach (var sg in steamSpyGames)
        {
            if (sg.AppId <= 0 || string.IsNullOrWhiteSpace(sg.Name)) continue;
            if (!appMap.ContainsKey(sg.AppId))
                appMap[sg.AppId] = sg.Name;
        }

        var newGames = appMap
            .Where(kv => !existingIds.Contains(kv.Key))
            .Where(kv => _filter.IsValidName(kv.Value))
            .Select(kv => new Game
            {
                SteamAppId = kv.Key,
                Name = kv.Value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            })
            .ToList();

        for (int i = 0; i < newGames.Count; i += _options.BasicImportBatchSize)
        {
            var batch = newGames.Skip(i).Take(_options.BasicImportBatchSize).ToList();
            await _games.BulkAddAsync(batch, ct);
            _logger.LogInformation("Базовий імпорт: {Imported} / {Total}", i + batch.Count, newGames.Count);
        }

        _logger.LogInformation("SteamSpy basic import complete: +{New} new games ({Total} in catalog map)",
            newGames.Count, appMap.Count);
        return newGames.Count;
    }

    public async Task ImportDetailsBatchAsync(List<int> appIdsToUpdate, SteamImportState state, CancellationToken ct = default)
    {
        int backoffSeconds = 120 * 1000;
        int requestsSinceLastPause = 0;

        int updatedCount = 0;
        int deletedCount = 0;
        int rateLimitHits = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("Початок обробки батчу на {TotalGames} ігор...", appIdsToUpdate.Count);

        for (int i = 0; i < appIdsToUpdate.Count; i++)
        {
            if (ct.IsCancellationRequested || !state.IsImportingDetails)
            {
                _logger.LogWarning("Цикл батчу примусово зупинено користувачем.");
                break;
            }

            int appId = appIdsToUpdate[i];

            if (i > 0 && i % 20 == 0)
            {
                _logger.LogInformation("Прогрес: {Current}/{Total}. Минуло часу: {Elapsed}",
                    i, appIdsToUpdate.Count, stopwatch.Elapsed.ToString(@"mm\:ss"));
            }

            try
            {
                var details = await _steamClient.GetAppDetailsAsync(appId);

                requestsSinceLastPause++;

                if (requestsSinceLastPause >= _options.ProphylacticPauseAfter)
                {
                    _logger.LogInformation("Профілактична пауза: досягнуто {Limit} запитів. Йдемо спати на {Mins} хв...",
                        _options.ProphylacticPauseAfter, _options.PauseAfterBatchMs / 60000);

                    await Task.Delay(_options.PauseAfterBatchMs, ct);
                    requestsSinceLastPause = 0;
                }

                if (details == null)
                {
                    await _games.DeleteBySteamIdAsync(appId);
                    deletedCount++;
                    _logger.LogInformation("Видалено {AppId} (Немає даних у Steam).", appId);
                    continue;
                }

                if (details.release_date == null)
                {
                    await _games.DeleteBySteamIdAsync(appId);
                    deletedCount++;
                    _logger.LogInformation("Видалено гру, яка ще не вийшла в реліз {AppId}: {Name} (Тип: {Type})", appId, details.name, details.type);
                    continue;
                }

                if (!_filter.IsValidType(details.type))
                {
                    await _games.DeleteBySteamIdAsync(appId);
                    deletedCount++;
                    _logger.LogInformation("Видалено не-гру {AppId}: {Name} (Тип: {Type})", appId, details.name, details.type);
                    continue;
                }

                var game = await _games.GetBySteamIdAsync(appId);
                if (game == null) continue;

                _mapper.ApplyDetails(game, details);

                if (details.developers?.Any() == true)
                {
                    var dev = await _games.GetOrCreateDeveloperAsync(details.developers.First());
                    game.DeveloperId = dev.DeveloperId;
                }
                if (details.publishers?.Any() == true)
                {
                    var pub = await _games.GetOrCreatePublisherAsync(details.publishers.First());
                    game.PublisherId = pub.PublisherId;
                }
                if (details.genres != null)
                {
                    game.Genres.Clear();
                    foreach (var steamGenre in details.genres)
                    {
                        if (string.IsNullOrEmpty(steamGenre.description)) continue;
                        var genre = await _games.GetOrCreateGenreAsync(steamGenre.description);
                        game.Genres.Add(genre);
                    }
                }

                await _games.UpdateAsync(game);
                updatedCount++;

                backoffSeconds = 120 * 1000;
                await Task.Delay(_options.DelayBetweenRequestsMs, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                rateLimitHits++;
                _logger.LogWarning("Rate Limit 429! Отримали бан на {AppId}. Чекаємо {Backoff} сек...", appId, backoffSeconds);

                await Task.Delay(backoffSeconds, ct);

                backoffSeconds = 120 * 1000;
                requestsSinceLastPause = 0;
                i--;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неочікувана помилка гри {AppId}", appId);
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Батч завершено за {Elapsed}. Оновлено ігор: {Updated}, Видалено сміття: {Deleted}, Спіймано 429 лімітів: {RateLimits}",
            stopwatch.Elapsed.ToString(@"mm\:ss"), updatedCount, deletedCount, rateLimitHits);
    }
}
