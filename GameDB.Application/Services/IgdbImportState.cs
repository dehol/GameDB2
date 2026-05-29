namespace GameDB.Application.Services;

public sealed class IgdbImportState
{
    private volatile bool _isImporting;

    public bool IsImporting
    {
        get => _isImporting;
        set => _isImporting = value;
    }

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int LastBatchSize { get; set; }
    public string? LastMessage { get; set; }
    public string? LastError { get; set; }
}
