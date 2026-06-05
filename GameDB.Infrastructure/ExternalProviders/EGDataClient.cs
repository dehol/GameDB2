using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace GameDB.Infrastructure.ExternalProviders;

/// <summary>
/// Клієнт до egdata.app — community API для каталогу Epic Games Store.
/// Документація: https://api.egdata.app
/// </summary>
public sealed class EGDataClient : IEGDataClient
{
    private readonly HttpClient _http;
    private readonly ILogger<EGDataClient> _logger;
    private readonly string _country;

    private const string BaseUrl = "https://api.egdata.app";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    

    public EGDataClient(
        HttpClient http,
        IOptions<EGDataImportOptions> options,
        ILogger<EGDataClient> logger)
    {
        _http    = http;
        _logger  = logger;
        _country = options.Value?.Country ?? "US"; 
    }

    public async Task<EGDataListResponseDto?> GetItemsPageAsync(
        int page, int limit, CancellationToken ct = default)
    {
        // Замінив country=US на динамічний заголовок country={_country}
        var url = $"{BaseUrl}/items?country={_country}&limit={limit}&page={page}" +
                  "&sortBy=releaseDate&sortDir=ASC";
        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EGData items page {Page} → {Status}", page, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return null;

            var result = JsonSerializer.Deserialize<EGDataListResponseDto>(json, JsonOpts);
            if (result != null)
            {
                // ФІКС: Перевіряємо, чи сервер взагалі щось прислав (до очищення)
                result.HasDataFromServer = result.Elements != null && result.Elements.Count > 0;

                if (result.Elements != null)
                {
                    int totalBefore = result.Elements.Count;

                    result.Elements.RemoveAll(item =>
                    (item.EntitlementType != "EXECUTABLE" && item.EntitlementType != "GAME") ||
                    item.Status != "ACTIVE" ||
                    item.Unsearchable ||
                    item.Categories == null ||
                    !item.Categories.Any(c =>
                        c.Path != null &&
                        (c.Path == "games" || c.Path.StartsWith("games/"))) ||  // ← sub-categories теж ок
                    item.ReleaseInfo == null ||
                    item.ReleaseInfo.Count == 0);                                                    

                    int removedCount = totalBefore - result.Elements.Count;
                    if (removedCount > 0)
                    {
                        _logger.LogDebug("EGData page {Page}: відфільтровано {Count} dlc. Лишилось ігор: {Remaining}", 
                            page, removedCount, result.Elements.Count);
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EGData items page {Page} — помилка", page);
            return null;
        }
    }

    public async Task<EGDataItemDto?> GetItemDetailsAsync(
        string itemId, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/items/{itemId}?country={_country}";
        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EGData item {Id} → {Status}", itemId, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return null;

            return JsonSerializer.Deserialize<EGDataItemDto>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EGData item {Id} — помилка", itemId);
            return null;
        }
    }

    public async Task<StorePriceInfo?> GetItemPriceAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            var offerUrl = $"{BaseUrl}/items/{itemId}/offer";

            var offerResponse = await _http.GetAsync(offerUrl, ct);
            if (!offerResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Offer request failed {Id} → {Status}", itemId, offerResponse.StatusCode);
            }

            var offerJson = await offerResponse.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(offerJson))
                return null;

            using var doc = JsonDocument.Parse(offerJson);

            var offerId = doc.RootElement
                .GetProperty("id")
                .GetString();

            if (string.IsNullOrWhiteSpace(offerId))
                return null;

            var priceUrl = $"{BaseUrl}/offers/{offerId}/price";

            var priceResponse = await _http.GetAsync(priceUrl, ct);
            if (!priceResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Price request failed {OfferId} → {Status}", offerId, priceResponse.StatusCode);
                return null;
            }

            var priceJson = await priceResponse.Content.ReadAsStringAsync(ct);

            var priceDoc = JsonDocument.Parse(priceJson);

            var price = priceDoc.RootElement
                .GetProperty("price");

            var originalPrice = price.GetProperty("originalPrice").GetDecimal() / 100m;
            var discountPrice = price.GetProperty("discountPrice").GetDecimal() / 100m;
            decimal discountPercent = 0; 
            if(originalPrice > 0)
                discountPercent = Math.Round(1 - (discountPrice / originalPrice), 2);

            return new StorePriceInfo(originalPrice, (short)(discountPercent * 100), _country);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EGData price error for {Id}", itemId);
            return null;
        }
    }
}