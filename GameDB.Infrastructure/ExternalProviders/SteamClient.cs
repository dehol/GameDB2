using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GameDB.Infrastructure.ExternalProviders;

public class SteamClient : ISteamClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private const string SteamApiUrl = "https://api.steampowered.com";
    
    public SteamClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
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
}