using System.Threading.RateLimiting;

namespace GameDB.Infrastructure.SteamSpy;

/// <summary>
/// Singleton enforcing SteamSpy appdetails limit: 1 request per second.
/// Shared by enrichment and price sync jobs.
/// </summary>
public sealed class SteamSpyRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit           = 1,
        TokensPerPeriod      = 1,
        ReplenishmentPeriod  = TimeSpan.FromSeconds(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit           = 1000,
        AutoReplenishment    = true,
    });

    public async ValueTask AcquireAsync(CancellationToken ct = default)
    {
        using var lease = await _limiter.AcquireAsync(permitCount: 1, ct);
        if (!lease.IsAcquired)
            throw new OperationCanceledException("SteamSpy rate limiter queue overflow.", ct);
    }

    public void Dispose() => _limiter.Dispose();
}
