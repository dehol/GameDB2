// FastIgdbImportService.cs
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services;

public class FastIgdbImportService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<FastIgdbImportService> _logger;

    private string? _cachedToken;
    private DateTime _tokenExpiry;

    public FastIgdbImportService(IServiceProvider serviceProvider, HttpClient httpClient, IConfiguration config, ILogger<FastIgdbImportService> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task ImportDetailsFastAsync(List<int> appIdsToUpdate, CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);

        _logger.LogInformation("🚀 IGDB Токен готовий. Починаємо турбо-імпорт ({Count} ігор)!", appIdsToUpdate.Count);

        using var semaphore = new SemaphoreSlim(3, 3);

        var tasks = appIdsToUpdate.Select(async appId =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                await ProcessSingleGameAsync(appId, games, ct);
            }
            finally
            {
                await Task.Delay(500, ct);
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            return;

        var clientId = _config["IGDB:ClientId"];
        var clientSecret = _config["IGDB:ClientSecret"];

        var authUrl = $"https://id.twitch.tv/oauth2/token?client_id={clientId}&client_secret={clientSecret}&grant_type=client_credentials";
        var authResponse = await _httpClient.PostAsync(authUrl, null, ct);
        var authData = await authResponse.Content.ReadFromJsonAsync<TwitchAuthResponse>(cancellationToken: ct);

        _cachedToken = authData!.access_token;
        _tokenExpiry = DateTime.UtcNow.AddDays(50);

        _httpClient.DefaultRequestHeaders.Remove("Client-ID");
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Add("Client-ID", clientId);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_cachedToken}");

        _logger.LogInformation("🔑 IGDB Токен отримано, дійсний до {Expiry}", _tokenExpiry);
    }

    private async Task ProcessSingleGameAsync(int appId, IGameRepository games, CancellationToken ct)
    {
        var game = await games.GetBySteamIdAsync(appId);
        if (game == null) return;

        try
        {
            var query = $"where external_games.category = 1 & external_games.uid = \"{appId}\"; fields name, summary, storyline, first_release_date, rating, rating_count, cover.image_id, genres.name, involved_companies.company.name, involved_companies.developer, involved_companies.publisher; limit 1;";
            var content = new StringContent(query, Encoding.UTF8, "text/plain");

            var response = await PostWithRetryAsync("https://api.igdb.com/v4/games", content, ct);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            var igdbGames = JsonSerializer.Deserialize<List<IgdbGameDto>>(jsonString);

            if (igdbGames == null || igdbGames.Count == 0)
            {
                _logger.LogInformation("IGDB: Спроба знайти {Name} за назвою (не знайдено за SteamId)", game.Name);
                var nameQuery = $"search \"{game.Name.Replace("\"", "")}\"; fields name, summary, storyline, first_release_date, rating, rating_count, cover.image_id, genres.name, involved_companies.company.name, involved_companies.developer, involved_companies.publisher; limit 1;";
                var nameContent = new StringContent(nameQuery, Encoding.UTF8, "text/plain");

                var nameResponse = await PostWithRetryAsync("https://api.igdb.com/v4/games", nameContent, ct);
                nameResponse.EnsureSuccessStatusCode();

                var nameJsonString = await nameResponse.Content.ReadAsStringAsync(ct);
                igdbGames = JsonSerializer.Deserialize<List<IgdbGameDto>>(nameJsonString);

                if (igdbGames == null || igdbGames.Count == 0)
                {
                    _logger.LogWarning("⚠️ [IGDB] Гру не знайдено ні за id, ні за назвою: {Name}", game.Name);
                    // Ставимо порожній рядок щоб не запитувати знову
                    game.Description = null;
                    game.UpdatedAt = DateTime.UtcNow;
                    await games.UpdateAsync(game);
                    return;
                }
            }

            var igdbData = igdbGames.First();

            game.Description = FormatAsSteamHtml(igdbData.summary, igdbData.storyline) ?? string.Empty;

            if (igdbData.cover?.image_id != null)
            {
                game.HeaderImage = $"https://images.igdb.com/igdb/image/upload/t_1080p/{igdbData.cover.image_id}.jpg";
                game.IconImage = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{igdbData.cover.image_id}.jpg";
            }

            if (igdbData.rating.HasValue)
                game.Rating = Math.Round(igdbData.rating.Value);

            if (igdbData.rating_count.HasValue)
                game.RatingCount = igdbData.rating_count.Value;

            if (igdbData.first_release_date.HasValue)
            {
                var releaseDateTime = DateTimeOffset.FromUnixTimeSeconds(igdbData.first_release_date.Value).DateTime;
                game.ReleaseDate = DateOnly.FromDateTime(releaseDateTime);
            }

            game.UpdatedAt = DateTime.UtcNow;

            if (igdbData.genres != null)
            {
                foreach (var g in igdbData.genres.Where(x => !string.IsNullOrEmpty(x.name)))
                {
                    var genre = await games.GetOrCreateGenreAsync(g.name!);
                    game.Genres.Add(genre);
                }
            }

            if (igdbData.involved_companies != null)
            {
                var devName = igdbData.involved_companies.FirstOrDefault(c => c.developer)?.company?.name;
                if (!string.IsNullOrEmpty(devName))
                {
                    var dev = await games.GetOrCreateDeveloperAsync(devName);
                    game.DeveloperId = dev.DeveloperId;
                }

                var pubName = igdbData.involved_companies.FirstOrDefault(c => c.publisher)?.company?.name;
                if (!string.IsNullOrEmpty(pubName))
                {
                    var pub = await games.GetOrCreatePublisherAsync(pubName);
                    game.PublisherId = pub.PublisherId;
                }
            }

            await games.UpdateAsync(game);
            _logger.LogInformation("✅ [IGDB->Steam] Оновлено: {Name}", game.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError("❌ Помилка з грою {Name}: {Message}", game.Name, ex.Message);
            await Task.Delay(1000, ct);
        }
    }

    private async Task<HttpResponseMessage> PostWithRetryAsync(string url, HttpContent content, CancellationToken ct)
    {
        var bodyText = await content.ReadAsStringAsync(ct);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var clonedContent = new StringContent(bodyText, Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync(url, clonedContent, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var delay = attempt * 2000;
                _logger.LogWarning("⏳ 429 — чекаємо {Delay}ms (спроба {Attempt}/3)", delay, attempt);
                await Task.Delay(delay, ct);
                continue;
            }

            return response;
        }

        throw new Exception($"429 після 3 спроб на {url}");
    }

    private string FormatAsSteamHtml(string? summary, string? storyline)
    {
        if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(storyline))
            return string.Empty;

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            var paragraphs = summary.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in paragraphs)
                sb.Append($"<p class=\"bb_paragraph\">{p.Trim()}</p>");
        }

        if (!string.IsNullOrWhiteSpace(storyline))
        {
            sb.Append("<h2 class=\"bb_tag\">Storyline</h2>");
            var paragraphs = storyline.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in paragraphs)
                sb.Append($"<p class=\"bb_paragraph\">{p.Trim()}</p>");
        }

        return sb.ToString();
    }
}