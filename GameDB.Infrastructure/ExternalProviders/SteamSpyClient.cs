using System.Net.Http.Json;
using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace GameDB.Infrastructure.ExternalProviders;

public sealed class SteamSpyClient : ISteamSpyClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamSpyClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _baseUrl = config["SteamSpy:BaseUrl"] ?? "https://steamspy.com/api.php";
    }

    public async Task<SteamSpyAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl.TrimEnd('/')}?request=appdetails&appid={appId}";
        var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        try
        {
            return JsonSerializer.Deserialize<SteamSpyAppDetailsDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyCollection<SteamSpyAppListItemDto>> GetAppListAsync(CancellationToken ct = default)
    {
        var url = $"{_baseUrl.TrimEnd('/')}?request=all";
        var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return [];

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return [];

        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, SteamSpyAppListItemDto>>(json, JsonOptions);
            if (data == null || data.Count == 0)
                return [];

            foreach (var (key, item) in data)
            {
                if (item is null) continue;
                if (item.AppId == 0 && int.TryParse(key, out var appId))
                    item.AppId = appId;
            }

            return data.Values
                .Where(item => item is not null &&
                               item.AppId > 0 &&
                               !string.IsNullOrWhiteSpace(item.Name))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
