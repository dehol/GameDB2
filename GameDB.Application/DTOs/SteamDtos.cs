using System.Text.Json.Serialization;

namespace GameDB.Application.DTOs;

public sealed class SteamAppListResponse
{
    public SteamAppListContainer Response { get; set; } = null!;
}

public sealed class SteamAppListContainer
{
    public List<SteamAppListItemDto> Apps { get; set; } = [];
    [JsonPropertyName("have_more_results")]
    public bool HaveMoreResults { get; set; }
    [JsonPropertyName("last_appid")]
    public int LastAppid { get; set; }
}

public sealed class SteamAppListItemDto
{
    public int Appid { get; set; }
    public string Name { get; set; } = string.Empty;
}