using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.ExternalProviders;

public class SteamClient : ISteamClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamClient> _logger;
    private readonly IConfiguration _configuration;
    private const string SteamApiUrl = "https://api.steampowered.com";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    public SteamClient(HttpClient httpClient, IConfiguration configuration, ILogger<SteamClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger     = logger;
    }

    public async Task<IReadOnlyList<int>> GetOwnedGameAppIdsAsync(string steamId, CancellationToken ct = default)
    {
        var key = _configuration["Steam:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return [];

        var url =
            $"{SteamApiUrl}/IPlayerService/GetOwnedGames/v1/?key={key}&steamid={steamId}" +
            "&include_appinfo=0&include_played_free_games=1";

        return await FetchAppIdListAsync(url, "games", ct);
    }

    public async Task<IReadOnlyList<int>> GetWishlistAppIdsAsync(string steamId, CancellationToken ct = default)
    {
        var key = _configuration["Steam:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return [];

        var url = $"{SteamApiUrl}/IWishlistService/GetWishlist/v1/?key={key}&steamid={steamId}";
        var fromItems = await FetchAppIdListAsync(url, "items", ct);
        if (fromItems.Count > 0)
            return fromItems;

        return await FetchAppIdListAsync(url, "games", ct);
    }

    private async Task<IReadOnlyList<int>> FetchAppIdListAsync(
        string url, string arrayProperty, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("response", out var responseEl))
                return [];

            if (!responseEl.TryGetProperty(arrayProperty, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var ids = new List<int>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("appid", out var appId))
                    ids.Add(appId.GetInt32());
                else if (item.ValueKind == JsonValueKind.Number)
                    ids.Add(item.GetInt32());
            }

            return ids;
        }
        catch
        {
            return [];
        }
    }
    public async Task<IReadOnlyCollection<SteamAppListItemDto>> GetAppListAsync(CancellationToken ct = default)
    {
        var totalResult = new List<SteamAppListItemDto>();
        int lastAppId = 0;
        bool haveMoreResults = true;
        int pageCounter = 1;

        while (haveMoreResults)
        {
            ct.ThrowIfCancellationRequested();

            // Формуємо URL згідно з документацією Steam API
            var url = $"https://api.steampowered.com/IStoreService/GetAppList/v1/?" +
                  $"key={_configuration["Steam:ApiKey"]}" +
                  $"&max_results=50000" +
                  $"&last_appid={lastAppId}" +
                  $"&include_games=true" +
                  $"&include_dlc=false" +
                  $"&include_software=false" +
                  $"&include_videos=false" +
                  $"&include_hardware=false";
            
            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Steam API: помилка запиту на сторінці {Page}. Статус: {Status}. Зупиняємо імпорт.",
                    pageCounter, response.StatusCode);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json) || json == "null" || json == "{}")
            {
                _logger.LogInformation("Steam API: отримано порожню відповідь на сторінці {Page}.", pageCounter);
                break;
            }

            SteamAppListResponse? apiResponse = null;
            try
            {
                apiResponse = JsonSerializer.Deserialize<SteamAppListResponse>(json, JsonOpts);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Steam API: не зміг розпарсити JSON на сторінці {Page}", pageCounter);
                break;
            }

            var resultData = apiResponse?.Response;
            if (resultData?.Apps == null || resultData.Apps.Count == 0)
            {
                _logger.LogInformation("Steam API: нових ігор більше немає.");
                break;
            }

            // Додаємо відфільтровані ігри (ігноруємо записи без імені чи з кривим ID)
            var validApps = resultData.Apps
                .Where(a => a.Appid > 0 && !string.IsNullOrWhiteSpace(a.Name))
                .ToList();

            totalResult.AddRange(validApps);

            _logger.LogInformation(
                "Steam API Пачка {Page}: отримано +{Count} ігор (валідно: {ValidCount}). Всього в пам'яті: {Total}", 
                pageCounter, resultData.Apps.Count, validApps.Count, totalResult.Count);

            // Оновлюємо стан для наступної ітерації циклу
            haveMoreResults = resultData.HaveMoreResults;
            
            lastAppId = resultData.LastAppid;
            pageCounter++;

            // Невеликий Delay, щоб поважати ліміти Steam (хоча при пачці у 50к запитів буде всього 4-5 ітерацій)
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }

        return totalResult;
    }
}
