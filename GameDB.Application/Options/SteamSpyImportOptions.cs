namespace GameDB.Application.Options;

public sealed class SteamSpyImportOptions
{
    public string BaseUrl { get; set; } = "https://steamspy.com/api.php";

    public int DelayBetweenRequestsMs { get; set; } = 1100;

    public int AppDetailsDelayMs { get; set; } = 1000;

    public int AllRequestDelayMs { get; set; } = 61000;

    public int MaxTagsPerGame { get; set; } = 15;

    public int EnrichmentBatchSize { get; set; } = 200;

    /// <summary>SteamSpy appdetails requests per second (official limit: 1; try 2–4 if 429 stays rare).</summary>
    public int AppDetailsRequestsPerSecond { get; set; } = 1;

    /// <summary>Parallel in-flight enrichments (match or stay below AppDetailsRequestsPerSecond).</summary>
    public int EnrichmentConcurrency { get; set; } = 1;

    /// <summary>Parallel in-flight price sync requests.</summary>
    public int PriceSyncConcurrency { get; set; } = 1;
}
