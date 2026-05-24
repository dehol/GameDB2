using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GameDB.Infrastructure.Steam;

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

    public async Task<List<SteamGameData>> GetAppListAsync()
    {
        bool have_more_results = true;
        int last_appid = 0;
        var allGames = new List<SteamGameData>();

        while(have_more_results)
        {
            var url = $"{SteamApiUrl}/IStoreService/GetAppList/v1/?key={_configuration["Steam:ApiKey"]}&max_results=50000&last_appid={last_appid}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SteamAppList>(content);

            if (result?.response?.apps is null) break;
            allGames.AddRange(result.response.apps);
            
            have_more_results = result.response.have_more_results;
            last_appid = result.response.last_appid;
            
            if (have_more_results) await Task.Delay(500);
        }
        return allGames;
    }

    public async Task<SteamAppDetailsData?> GetAppDetailsAsync(int appId)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english"; 
        var response = await _httpClient.GetAsync(url);
        
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("Steam Rate Limit", null, System.Net.HttpStatusCode.TooManyRequests);
        }

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        if (root.TryGetProperty(appId.ToString(), out var appElement))
        {
            if (appElement.TryGetProperty("success", out var successElement) && successElement.GetBoolean())
            {
                if (appElement.TryGetProperty("data", out var dataElement))
                {
                    return JsonSerializer.Deserialize<SteamAppDetailsData>(dataElement.GetRawText());
                }
            }
        }
        return null;
    }
}