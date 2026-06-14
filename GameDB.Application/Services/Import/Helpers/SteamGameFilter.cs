namespace GameDB.Application.Services;

public class SteamGameFilter
{
    private readonly string[] _skipEndsWith = 
    { 
        " Soundtrack", " OST", " DLC", " Demo", " Beta", 
        " Test", " Playtest", " SDK", " Localization" 
    };

    private readonly string[] _skipContains = 
    { 
        " Soundtrack -", "Original Soundtrack", "Music Pack", " - DLC", 
        "Downloadable Content", " Pack", "Costume Pack", "Skin Pack", 
        "Character Pack", "Weapon Pack", "Map Pack", "Level Pack", 
        "Content Pack", " - Demo", " - Beta", "Dedicated Server", 
        "Artbook", "Art Book", "Digital Art", "Wallpaper", "Screensaver", 
        "Bonus Content", "Making of", " Editor", "Mod Tools", 
        "Creation Kit", "Language Pack" 
    };

    public bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        
        if (_skipEndsWith.Any(w => name.EndsWith(w, StringComparison.OrdinalIgnoreCase))) return false;
        if (_skipContains.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) return false;
        
        return true;
    }
}