using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GameDB.Infrastructure.ExternalProviders;

public class ItadClient : IItadClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public ItadClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _apiKey = config["ITAD:ApiKey"] ?? throw new ArgumentNullException("ITAD ApiKey is missing in appsettings.json");
    }

    public async Task<Dictionary<string, string>> GetUuidsBySteamIdsAsync(List<int> steamIds, CancellationToken ct = default)
    {
        // 1. Правильний ендпоінт ITAD для масового пошуку по Steam (ShopId = 61)
        var url = $"https://api.isthereanydeal.com/lookup/id/shop/61/v1?key={_apiKey}";
        
        // 2. Steam IDs треба передавати з префіксом "app/" (наприклад "app/220")
        var formattedIds = steamIds.Select(id => $"app/{id}").ToList();
        var content = new StringContent(JsonSerializer.Serialize(formattedIds), Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(url, content, ct);
        
        if (!response.IsSuccessStatusCode) 
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ [ITAD UUID ERROR] Статус: {response.StatusCode}");
            Console.WriteLine($"Деталі: {errorText}\n");
            Console.ResetColor();
            return new Dictionary<string, string>();
        }

        var jsonString = await response.Content.ReadAsStringAsync(ct);
        var result = new Dictionary<string, string>();
        
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            
            // ITAD повертає JSON, де ключі - це "app/220", а значення - UUID
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Відкидаємо "app/", щоб у словнику залишився чистий ID гри (напр. "220")
                string cleanId = prop.Name.Replace("app/", "").Replace("sub/", "");
                
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    result[cleanId] = prop.Value.GetString()!;
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ [ITAD PARSE ERROR] Помилка обробки UUIDs: {ex.Message}");
            Console.ResetColor();
        }

        return result;
    }

    public async Task<List<ItadPriceResponse>> GetPricesAsync(List<string> itadUuids, CancellationToken ct = default)
    {
        var url = $"https://api.isthereanydeal.com/games/prices/v3?key={_apiKey}&country=UA";
        var content = new StringContent(JsonSerializer.Serialize(itadUuids), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ [ITAD PRICES ERROR] Статус: {response.StatusCode}");
            Console.WriteLine($"Деталі: {errorText}\n");
            Console.ResetColor();
            return new List<ItadPriceResponse>();
        }

        return await response.Content.ReadFromJsonAsync<List<ItadPriceResponse>>(cancellationToken: ct) 
               ?? new List<ItadPriceResponse>();
    }
}