using System.Net;
using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Infrastructure.SteamSpy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameDB.Infrastructure.ExternalProviders;

public sealed class SteamSpyClient : ISteamSpyClient
{
    private readonly HttpClient _httpClient;
    private readonly SteamSpyRateLimiter _rateLimiter;
    private readonly SteamSpyImportOptions _options;
    private readonly ILogger<SteamSpyClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamSpyClient(
        HttpClient httpClient,
        IOptions<SteamSpyImportOptions> options,
        SteamSpyRateLimiter rateLimiter,
        ILogger<SteamSpyClient> logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _options = options.Value;
        _logger = logger;
    }

    public Task<SteamSpyAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken ct = default)
        => GetAppDetailsWithRetryAsync(appId, ct);

    public async Task<IReadOnlyCollection<SteamSpyAppListItemDto>> GetAppListPageAsync(int page, CancellationToken ct = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}?request=all&page={page}";
        var response = await SendWithRetryAsync(url, ct, useRateLimiter: false);
        if (response is null)
            return [];

        return ParseAppList(response);
    }

    public async Task<IReadOnlyCollection<SteamSpyAppListItemDto>> GetAllAppsAsync(CancellationToken ct = default)
    {
        var all = new Dictionary<int, SteamSpyAppListItemDto>();
        var page = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await GetAppListPageAsync(page, ct);
            if (batch.Count == 0)
                break;

            foreach (var item in batch)
            {
                if (item.AppId > 0 && !all.ContainsKey(item.AppId))
                    all[item.AppId] = item;
            }

            _logger.LogInformation("SteamSpy all: page {Page}, +{Count} apps, total {Total}",
                page, batch.Count, all.Count);

            page++;
            if (batch.Count < 1000)
                break;

            await Task.Delay(_options.AllRequestDelayMs, ct);
        }

        return all.Values.ToList();
    }

    private async Task<SteamSpyAppDetailsDto?> GetAppDetailsWithRetryAsync(int appId, CancellationToken ct)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}?request=appdetails&appid={appId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await _rateLimiter.AcquireAsync(ct);

            using var response = await _httpClient.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
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

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * (attempt + 1));
                _logger.LogWarning("SteamSpy 429 for AppId {AppId}, retry after {Delay}s", appId, retryAfter.TotalSeconds);
                await Task.Delay(retryAfter, ct);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
                continue;
            }

            _logger.LogWarning("SteamSpy appdetails failed for AppId {AppId}: {Status}", appId, response.StatusCode);
            return null;
        }

        return null;
    }

    private async Task<string?> SendWithRetryAsync(string url, CancellationToken ct, bool useRateLimiter)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (useRateLimiter)
                await _rateLimiter.AcquireAsync(ct);

            using var response = await _httpClient.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
                _logger.LogWarning("SteamSpy 429 for {Url}, retry after {Delay}s", url, retryAfter.TotalSeconds);
                await Task.Delay(retryAfter, ct);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
                continue;
            }

            _logger.LogWarning("SteamSpy request failed: {Url} {Status}", url, response.StatusCode);
            return null;
        }

        return null;
    }

    private static IReadOnlyCollection<SteamSpyAppListItemDto> ParseAppList(string json)
    {
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
                .ToList()!;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
