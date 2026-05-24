namespace GameDB.Application.DTOs;

public class TwitchAuthResponse
{
    public string access_token { get; set; } = null!;
}

public class IgdbGameDto
{
    public string? name { get; set; }
    public string? summary { get; set; }
    public string? storyline { get; set; } // Додатковий текст від IGDB
    public long? first_release_date { get; set; } // В IGDB це Unix Time (секунди)
    public double? rating { get; set; } // Оцінка гравців/критиків (0-100)
    public int? rating_count { get; set; } // Кількість відгуків
    public IgdbCover? cover { get; set; }
    public List<IgdbGenre>? genres { get; set; }
    public List<IgdbCompanyWrap>? involved_companies { get; set; }
}

public class IgdbCover { public string? image_id { get; set; } }
public class IgdbGenre { public string? name { get; set; } }
public class IgdbCompanyWrap 
{ 
    public IgdbCompany? company { get; set; } 
    public bool developer { get; set; } // Флаг розробника
    public bool publisher { get; set; } // Флаг видавця
}
public class IgdbCompany { public string? name { get; set; } }