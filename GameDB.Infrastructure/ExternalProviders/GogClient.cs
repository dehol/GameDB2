using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Globalization;
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

    public async Task<StorePriceInfo?> GetItemPriceAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            var offerUrl = $"https://api.gog.com/products/{itemId}/prices?countryCode=US";

            var offerResponse = await _http.GetAsync(offerUrl, ct);
            if (!offerResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Offer request failed {Id} → {Status}", itemId, offerResponse.StatusCode);
                return null;
            }

            var json = await offerResponse.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var doc = JsonDocument.Parse(json);

            var priceObj = doc.RootElement
                .GetProperty("_embedded")
                .GetProperty("prices")[0];

            var basePriceStr = priceObj.GetProperty("basePrice").GetString();
            var finalPriceStr = priceObj.GetProperty("finalPrice").GetString();

            decimal originalPrice = ParseGogPrice(basePriceStr!);
            decimal discountPrice = ParseGogPrice(finalPriceStr!);


            var discountPercent = originalPrice > 0 ? Math.Round(
                (originalPrice - discountPrice) / originalPrice, 2
            ) : 0;

            return new StorePriceInfo(originalPrice / 100m, (short)(discountPercent * 100), "US", BuildStoreUrl(itemId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GOG price error for {Id}", itemId);
            return null;
        }
    }
    private static string BuildStoreUrl(string slugOrId)
        => $"https://www.gog.com/game/{slugOrId}";
    static decimal ParseGogPrice(string value)
    {
        var number = value.Split(' ')[0];
        return decimal.Parse(number, CultureInfo.InvariantCulture);
    }
}