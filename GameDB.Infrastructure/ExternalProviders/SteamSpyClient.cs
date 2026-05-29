using System.Net.Http.Json;
using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;

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
}
