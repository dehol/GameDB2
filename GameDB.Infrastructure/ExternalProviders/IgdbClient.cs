using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Infrastructure.Igdb;
using Microsoft.Extensions.Configuration;

namespace GameDB.Infrastructure.ExternalProviders;

public class IgdbClient : IIgdbClient
{
    private readonly HttpClient      _httpClient;
    private readonly IgdbRateLimiter _rateLimiter;
    private readonly string          _clientId;
    private readonly string          _clientSecret;

    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private string?  _token;
    private DateTime _tokenExpiryUtc;

    public IgdbClient(HttpClient httpClient, IConfiguration config, IgdbRateLimiter rateLimiter)
    {
        _httpClient   = httpClient;
        _rateLimiter  = rateLimiter;
        _clientId     = config["IGDB:ClientId"]     ?? string.Empty;
        _clientSecret = config["IGDB:ClientSecret"] ?? string.Empty;
    }

    // ------------------------------------------------------------------ auth

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_token) && _tokenExpiryUtc > DateTime.UtcNow.AddMinutes(1))
            return;

        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            throw new InvalidOperationException("IGDB ClientId/ClientSecret missing in configuration");

        var url  = $"https://id.twitch.tv/oauth2/token?client_id={_clientId}&client_secret={_clientSecret}&grant_type=client_credentials";
        var resp = await _httpClient.PostAsync(url, null, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var dto  = JsonSerializer.Deserialize<TwitchAuthResponse>(json, _jsonOptions)
                   ?? throw new InvalidOperationException("Failed to parse Twitch auth response");

        _token          = dto.access_token;
        _tokenExpiryUtc = DateTime.UtcNow.AddMinutes(45);
    }

    // ------------------------------------------------------------------ public API

    public async Task<IgdbGameDto?> GetBySteamIdAsync(int steamAppId, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);

        // Крок 1: IGDB game ID по Steam App ID
        await _rateLimiter.AcquireAsync(ct);
        var lookupResp = await SendQueryAsync(
            "https://api.igdb.com/v4/external_games",
            $"fields game; where category = 1 & uid = \"{steamAppId}\"; limit 1;",
            ct);

        if (lookupResp is null) return null;

        var externals = JsonSerializer.Deserialize<List<ExternalGameDto>>(lookupResp, _jsonOptions);
        var igdbId    = externals?.FirstOrDefault()?.game;
        if (igdbId is null) return null;

        // Крок 2: деталі по IGDB ID
        await _rateLimiter.AcquireAsync(ct);
        var detailResp = await SendQueryAsync(
            "https://api.igdb.com/v4/games",
            $"""
            fields name, summary, storyline, first_release_date, rating, rating_count,
                   cover.image_id, genres.name,
                   involved_companies.company.name,
                   involved_companies.developer,
                   involved_companies.publisher;
            where id = {igdbId};
            limit 1;
            """,
            ct);

        if (detailResp is null) return null;

        var list = JsonSerializer.Deserialize<List<IgdbGameDto>>(detailResp, _jsonOptions);
        return list?.FirstOrDefault();
    }

    public async Task<IReadOnlyList<IgdbGameDto>> SearchGamesAsync(string gameName, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);
        await _rateLimiter.AcquireAsync(ct);

        var resp = await SendQueryAsync(
            "https://api.igdb.com/v4/games",
            $"""
            search "{gameName.Replace("\"", "\\\"")}";
            fields name, summary, storyline, first_release_date, rating, rating_count,
                   cover.image_id, genres.name,
                   involved_companies.company.name,
                   involved_companies.developer,
                   involved_companies.publisher;
            limit 5;
            """,
            ct);

        if (resp is null) return Array.Empty<IgdbGameDto>();

        try
        {
            return JsonSerializer.Deserialize<List<IgdbGameDto>>(resp, _jsonOptions)
                   ?? new List<IgdbGameDto>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IGDB parse error: {ex.Message}");
            return Array.Empty<IgdbGameDto>();
        }
    }

    // ------------------------------------------------------------------ helper

    // Retries once on 429 with a 1-second back-off before giving up.
    private async Task<string?> SendQueryAsync(string url, string query, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(query, Encoding.UTF8, "text/plain")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            if (!string.IsNullOrEmpty(_clientId))
                request.Headers.Add("Client-ID", _clientId);

            var resp = await _httpClient.SendAsync(request, ct);

            if (resp.IsSuccessStatusCode)
                return await resp.Content.ReadAsStringAsync(ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt == 0)
            {
                // Respect Retry-After if present, otherwise wait 1s
                var retryAfter = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                await Task.Delay(retryAfter, ct);
                continue;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"IGDB request failed [{url}]: {resp.StatusCode} {body}");
            return null;
        }

        return null;
    }
}