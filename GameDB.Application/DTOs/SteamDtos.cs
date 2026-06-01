namespace GameDB.Application.DTOs;

public sealed class SteamAppListResponse
{
    public SteamAppListContainer Response { get; set; } = null!;
}

public sealed class SteamAppListContainer
{
    public List<SteamAppListItemDto> Apps { get; set; } = [];
    public bool HaveMoreResults { get; set; }
    public int LastAppid { get; set; }
}

public sealed class SteamAppListItemDto
{
    public int Appid { get; set; }
    public string Name { get; set; } = string.Empty;
}