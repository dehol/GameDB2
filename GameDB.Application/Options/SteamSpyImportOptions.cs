namespace GameDB.Application.Options;

public sealed class SteamSpyImportOptions
{
    public string BaseUrl { get; set; } = "https://steamspy.com/api.php";

    public int DelayBetweenRequestsMs { get; set; } = 100;

    public int MaxTagsPerGame { get; set; } = 15;
    public int BasicImportBatchSize {get; set;} = 200;
}
