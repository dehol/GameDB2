namespace GameDB.Application.Services;

public sealed class GameEnrichmentImportState
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
    public string? LastWarning { get; set; }
    public bool OverwriteExisting { get; set; }
    public int OverwriteSkip { get; set; }

    public int SuccessCount { get; private set; }
    public int ErrorCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int RateLimitCount { get; private set; }
    public string? LastBatchSummary { get; private set; }

    public void ResetCounters()
    {
        SuccessCount = 0;
        ErrorCount = 0;
        SkippedCount = 0;
        RateLimitCount = 0;
        LastBatchSummary = null;
        LastWarning = null;
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
}
