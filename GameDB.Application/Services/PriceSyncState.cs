namespace GameDB.Application.Services;

public sealed class PriceSyncState
{
    private volatile bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        set => _isRunning = value;
    }

    public int ProcessedGames { get; set; }
    public int TotalGames { get; set; }
    public int LastBatchSize { get; set; }
    public string? LastMessage { get; set; }
    public string? LastError { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public CancellationTokenSource? Cts { get; set; }

    public void ResetProgress(int total)
    {
        ProcessedGames = 0;
        TotalGames = total;
        LastBatchSize = 0;
        LastMessage = null;
        LastError = null;
        StartedAt = DateTime.UtcNow;
        FinishedAt = null;
    }

    public void MarkFinished(string message)
    {
        IsRunning = false;
        FinishedAt = DateTime.UtcNow;
        LastMessage = message;
    }
}
