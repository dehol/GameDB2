using System.Text.Json;
using GameDB.Application.DTOs.Auth;

namespace GameDB.Web.Services;

public sealed class SteamPlayerService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    public SteamPlayerService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    public async Task<SteamPlayerInfoDto?> GetPlayerAsync(string steamId, CancellationToken ct = default)
    {
        var apiKey = _config["Steam:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        try
        {
            var http = _httpFactory.CreateClient();
            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={apiKey}&steamids={steamId}";
            using var doc = await JsonDocument.ParseAsync(
                await http.GetStreamAsync(url, ct), cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("response", out var response)
                || !response.TryGetProperty("players", out var players)
                || players.GetArrayLength() == 0)
                return null;

            var p = players[0];
            var name = p.TryGetProperty("personaname", out var pn) ? pn.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var avatar = p.TryGetProperty("avatarfull", out var av) ? av.GetString() : null;
            var profileUrl = p.TryGetProperty("profileurl", out var pu) && pu.GetString() is { } u
                ? u
                : $"https://steamcommunity.com/profiles/{steamId}";

            return new SteamPlayerInfoDto(name, avatar, profileUrl);
        }
        catch
        {
            return null;
        }
    }
}
