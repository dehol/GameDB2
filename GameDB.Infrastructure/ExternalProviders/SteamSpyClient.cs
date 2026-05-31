using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.ExternalProviders;

public sealed class SteamSpyClient : ISteamSpyClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamSpyClient> _logger;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamSpyClient(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<SteamSpyClient> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
        _baseUrl    = config["SteamSpy:BaseUrl"] ?? "https://steamspy.com/api.php";
    }

    public async Task<SteamSpyAppDetailsDto?> GetAppDetailsAsync(
        int appId, CancellationToken ct = default)
    {
        var url      = $"{_baseUrl.TrimEnd('/')}?request=appdetails&appid={appId}";
        var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        try
        {
            json = json.Replace("\"tags\":[]", "\"tags\":null");
            return JsonSerializer.Deserialize<SteamSpyAppDetailsDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "SteamSpy: не зміг розпарсити відповідь для AppId={AppId}", appId);
            return null;
        }
    }

    public async Task<IReadOnlyCollection<SteamSpyAppListItemDto>> GetAppListAsync(
        CancellationToken ct = default)
    {
        var result = new Dictionary<int, SteamSpyAppListItemDto>();

        for (var page = 0; ; page++)
        {
            ct.ThrowIfCancellationRequested();

            var url      = $"{_baseUrl.TrimEnd('/')}?request=all&page={page}";
            var response = await _httpClient.GetAsync(url, ct);

            // Polly вже відпрацював retry — якщо все одно не 200, виходимо
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "SteamSpy page {Page} повернув {Status}, зупиняємо пагінацію",
                    page, response.StatusCode);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(json) || json == "null" || json == "{}")
                break; // порожня сторінка — кінець пагінації

            Dictionary<string, SteamSpyAppListItemDto>? data = null;
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, SteamSpyAppListItemDto>>(
                    json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "SteamSpy page {Page} — не зміг розпарсити", page);
                break;
            }

            if (data is null || data.Count == 0)
                break;

            var added = 0;
            foreach (var (key, item) in data)
            {
                if (item is null) continue;

                if (item.AppId == 0 && int.TryParse(key, out var parsedId))
                    item.AppId = parsedId;

                if (item.AppId > 0
                    && !string.IsNullOrWhiteSpace(item.Name)
                    && result.TryAdd(item.AppId, item))
                {
                    added++;
                }
            }

            _logger.LogInformation(
                "SteamSpy page {Page}: +{Added} ігор, всього {Total}", page, added, result.Count);

            // Затримка між сторінками щоб не словити 429
            // (Polly обробить якщо все одно прийде, але краще не доводити)
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        }

        return result.Values.ToList();
    }
}