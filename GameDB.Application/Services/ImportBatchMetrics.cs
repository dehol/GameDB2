namespace GameDB.Application.Services;

public sealed class ImportBatchMetrics
{
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int SkippedCount { get; set; }
    public int RateLimitCount { get; set; }

    public void Add(ImportBatchMetrics other)
    {
        SuccessCount += other.SuccessCount;
        ErrorCount += other.ErrorCount;
        SkippedCount += other.SkippedCount;
        RateLimitCount += other.RateLimitCount;
    }

    public string ToSummary()
        => $"OK={SuccessCount} SKIP={SkippedCount} ERR={ErrorCount} 429={RateLimitCount}";
}
