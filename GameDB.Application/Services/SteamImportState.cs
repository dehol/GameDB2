namespace GameDB.Application.Services;

public sealed class SteamImportState
{
    private volatile bool _isImportingDetails;

    public bool IsImportingDetails
    {
        get => _isImportingDetails;
        set => _isImportingDetails = value;
    }

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int LastBatchSize { get; set; }
    public string? LastMessage { get; set; }
    public string? LastError { get; set; }
}
