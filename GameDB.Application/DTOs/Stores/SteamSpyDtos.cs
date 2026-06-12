using System.Text.Json.Serialization;

namespace GameDB.Application.DTOs;

public sealed class SteamSpyAppDetailsDto
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("developer")]
    public string? Developer { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("positive")]
    public int Positive { get; set; }

    [JsonPropertyName("negative")]
    public int Negative { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("initialprice")]
    public string? InitialPrice { get; set; }

    [JsonPropertyName("discount")]
    public string? Discount { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, int>? Tags { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class SteamSpyAppListItemDto
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
