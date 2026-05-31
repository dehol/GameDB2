using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;
using GameDB.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public class SteamSpyImportService
{
    public const int SteamShopId = 1;

    private const string HeaderImageTemplate = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{0}/header.jpg";
    private const string IconImageTemplate   = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{0}/capsule_184x69.jpg";
    private const string StoreUrlTemplate    = "https://store.steampowered.com/app/{0}/";

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
        _games    = games;
        _steamSpy = steamSpy;
        _filter   = filter;
        _logger   = logger;
        _options  = options.Value;
    }

    public async Task<int> ImportBasicGamesAsync(CancellationToken ct = default)
    {
        IReadOnlyCollection<SteamSpyAppListItemDto> steamSpyGames = [];
        try
        {
            steamSpyGames = await _steamSpy.GetAppListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SteamSpy app list import failed");
        }

        // Репозиторій повертає рядки — конвертуємо в int тут, у Steam-сервісі
        var existingRaw = await _games.GetExistingExternalIdsAsync(SteamShopId, ct);
        var existingIds = new HashSet<int>(existingRaw.Count);
        foreach (var s in existingRaw)
            if (int.TryParse(s, out var id))
                existingIds.Add(id);

        var appMap = new Dictionary<int, string>();
        foreach (var sg in steamSpyGames)
        {
            if (sg.AppId <= 0 || string.IsNullOrWhiteSpace(sg.Name)) continue;
            appMap.TryAdd(sg.AppId, sg.Name);
        }

        var newGames = appMap
            .Where(kv => !existingIds.Contains(kv.Key))
            .Where(kv => _filter.IsValidName(kv.Value))
            .Select(kv => BuildBasicGame(kv.Key, kv.Value))
            .ToList();

        for (int i = 0; i < newGames.Count; i += _options.BasicImportBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = newGames.Skip(i).Take(_options.BasicImportBatchSize).ToList();
            await _games.BulkAddAsync(batch, ct);
            _logger.LogInformation("Базовий імпорт: {Imported} / {Total}", i + batch.Count, newGames.Count);
        }

        return newGames.Count;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Game BuildBasicGame(int appId, string name)
    {
        var now = DateTime.UtcNow;
        var game = new Game
        {
            Name         = name,
            HeaderImage  = BuildHeaderImageUrl(appId),
            IconImage    = BuildIconImageUrl(appId),
            ImportStatus = GameImportStatus.Basic,
            CreatedAt    = now,
            UpdatedAt    = now
        };
        game.ExternalIds.Add(new GameExternalId
        {
            ShopId      = SteamShopId,
            ExternalId  = appId.ToString(),
            ExternalUrl = BuildStoreUrl(appId),
            CreatedAt   = now
        });
        return game;
    }

    public static string BuildHeaderImageUrl(int appId) => string.Format(HeaderImageTemplate, appId);
    public static string BuildIconImageUrl(int appId)   => string.Format(IconImageTemplate,   appId);
    public static string BuildStoreUrl(int appId)       => string.Format(StoreUrlTemplate,    appId);
}