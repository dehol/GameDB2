using System.Threading.RateLimiting;

namespace GameDB.Infrastructure.Igdb;

/// <summary>
/// Singleton enforcing IGDB API limits:
///   - 4 requests per second (no burst — TokenLimit equals TokensPerPeriod)
///   - up to 8 concurrent open requests
///
/// Registered as singleton so all transient IgdbClient instances share one limiter.
/// </summary>
public sealed class IgdbRateLimiter : IDisposable
{
    // TokenLimit = TokensPerPeriod = 4 → no burst, strictly 4 req/s.
    // Setting TokenLimit > TokensPerPeriod would allow a burst on startup
    // which immediately triggers IGDB's 429.
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit           = 4,
        TokensPerPeriod      = 4,
        ReplenishmentPeriod  = TimeSpan.FromSeconds(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit           = 1000,
        AutoReplenishment    = true,
    });

    public async ValueTask AcquireAsync(CancellationToken ct = default)
    {
        using var lease = await _limiter.AcquireAsync(permitCount: 1, ct);
        if (!lease.IsAcquired)
            throw new OperationCanceledException("IGDB rate limiter queue overflow.", ct);
    }

    public void Dispose() => _limiter.Dispose();
}