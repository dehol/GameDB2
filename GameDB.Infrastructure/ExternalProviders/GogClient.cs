using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
namespace GameDB.Infrastructure.ExternalProviders;

public sealed class GogClient : IGogClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GogClient> _logger;

    private const string ListBaseUrl    = "https://www.gog.com/games/ajax/filtered";
    private const string DetailsBaseUrl = "https://api.gog.com/products";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public GogClient(HttpClient http, ILogger<GogClient> logger)
    {
        _http   = http;
        _logger = logger;
    }

    public async Task<GogFilteredResponseDto?> GetGamesPageAsync(
        int page, CancellationToken ct = default)
    {
        var url = $"{ListBaseUrl}?mediaType=game&page={page}";
        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GOG list page {Page} → {Status}", page, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return null;

            return JsonSerializer.Deserialize<GogFilteredResponseDto>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GOG list page {Page} — помилка", page);
            return null;
        }
    }

    public async Task<GogProductDetailsDto?> GetProductDetailsAsync(
        string productId, CancellationToken ct = default)
    {
        var url = $"{DetailsBaseUrl}/{productId}";
        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GOG product {Id} → {Status}", productId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return null;

            return JsonSerializer.Deserialize<GogProductDetailsDto>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GOG product {Id} — помилка", productId);
            return null;
        }
    }
}