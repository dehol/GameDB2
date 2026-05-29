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
    public string? LastWarning { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public int SuccessCount { get; private set; }
    public int ErrorCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int RateLimitCount { get; private set; }
    public string? LastBatchSummary { get; private set; }

    public CancellationTokenSource? Cts { get; set; }

    public void ResetProgress(int total)
    {
        ProcessedGames = 0;
        TotalGames = total;
        LastBatchSize = 0;
        LastMessage = null;
        LastError = null;
        LastWarning = null;
        SuccessCount = 0;
        ErrorCount = 0;
        SkippedCount = 0;
        RateLimitCount = 0;
        LastBatchSummary = null;
        StartedAt = DateTime.UtcNow;
        FinishedAt = null;
    }

    public void RecordBatch(ImportBatchMetrics metrics, TimeSpan elapsed)
    {
        SuccessCount += metrics.SuccessCount;
        ErrorCount += metrics.ErrorCount;
        SkippedCount += metrics.SkippedCount;
        RateLimitCount += metrics.RateLimitCount;
        LastBatchSummary = $"{metrics.ToSummary()} · {elapsed:mm\\:ss}";

        if (metrics.ErrorCount > 0 || metrics.RateLimitCount > 0)
            LastWarning = LastBatchSummary;
    }

    public void MarkFinished(string message)
    {
        IsRunning = false;
        FinishedAt = DateTime.UtcNow;
        LastMessage = message;
    }
}
