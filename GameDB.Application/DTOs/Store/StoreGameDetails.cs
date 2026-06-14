namespace GameDB.Application.DTOs.Store;

public sealed class StoreGameDetails
{
    public required string ExternalId     { get; init; }
    public required string Name           { get; init; }
    public string?         Developer      { get; init; }
    public string?         Publisher      { get; init; }
    public string[]        Genres         { get; init; } = [];
    public string[]        Tags           { get; init; } = [];
    public double?         Rating         { get; init; }
    public int?            RatingCount    { get; init; }
    public string?         HeaderImageUrl { get; init; }
    public string?         IconImageUrl   { get; init; }

    /// <summary>
    /// Опис гри від постачальника. Може містити HTML-розмітку (Steam/GOG).
    /// Null якщо постачальник не повертає опис (наприклад SteamSpy).
    /// </summary>
    public string?         Description    { get; init; }
}