using System.Diagnostics;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public class SteamSpyImportService
{
    private readonly IGameRepository _games;
    private readonly ISteamSpyClient _steamSpy;
    private readonly SteamGameFilter _filter;
    private readonly ILogger<SteamSpyImportService> _logger;
    private readonly SteamSpyImportOptions _options;

    public SteamSpyImportService(
        IGameRepository games, 
        ISteamSpyClient steamSpy,
        SteamGameFilter filter, 
        ILogger<SteamSpyImportService> logger,
        IOptions<SteamSpyImportOptions> options)
    {
        _games = games;
        _steamSpy = steamSpy;
        _filter = filter;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<int> ImportBasicGamesAsync()
    {
        IReadOnlyCollection<SteamSpyAppListItemDto> steamSpyGames = [];
        try
        {
            steamSpyGames = await _steamSpy.GetAppListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SteamSpy app list import failed");
        }
        var existingIds = await _games.GetExistingSteamAppIdsAsync();

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
            await _games.BulkAddAsync(batch);
            _logger.LogInformation("Базовий імпорт: {Imported} / {Total}", i + batch.Count, newGames.Count);
        }
        return newGames.Count;
    }

}
