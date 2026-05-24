namespace GameDB.Application.DTOs;

public class SteamAppDetailsData
{
    public string? type { get; set; } // "game", "dlc" тощо
    public string? name { get; set; }
    public string? detailed_description { get; set; }
    public string? short_description { get; set; }
    public string? header_image { get; set; }
    public bool is_free { get; set; }         // Нове поле
    public string? capsule_image { get; set; } // Звичайна іконка
    public string? capsule_imagev5 { get; set; } // Іконка покращеної якості
    
    public SteamMetacritic? metacritic { get; set; }
    public SteamRecommendations? recommendations { get; set; }
    public string[]? developers { get; set; }
    public string[]? publishers { get; set; }
    public SteamReleaseDate? release_date { get; set; }
    public SteamGenreDto[]? genres { get; set; }
}

public class SteamMetacritic { public int score { get; set; } }
public class SteamRecommendations { public int total { get; set; } }
public class SteamReleaseDate { public bool coming_soon { get; set; } public string? date { get; set; } }
public class SteamGenreDto { public string? id { get; set; } public string? description { get; set; } }