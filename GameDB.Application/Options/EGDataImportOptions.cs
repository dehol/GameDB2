namespace GameDB.Application.Options;

public sealed class EGDataImportOptions
{
    /// <summary>Затримка між запитами до egdata.app API (мс). Default = 300.</summary>
    public int DelayBetweenRequestsMs { get; set; } = 0;

    /// <summary>Країна для цін. Default = US.</summary>
    public string Country { get; set; } = "US";
}