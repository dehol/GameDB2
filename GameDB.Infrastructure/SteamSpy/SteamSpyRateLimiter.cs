using System.Threading.RateLimiting;
using GameDB.Application.Options;
using Microsoft.Extensions.Options;

namespace GameDB.Infrastructure.SteamSpy;

/// <summary>
/// Singleton rate limiter for SteamSpy appdetails. Shared by enrichment and price sync.
/// </summary>
public sealed class SteamSpyRateLimiter : IDisposable
{
    private readonly object _sync = new();
    private TokenBucketRateLimiter _limiter;
    private int _requestsPerSecond;

    public SteamSpyRateLimiter(IOptionsMonitor<SteamSpyImportOptions> options)
    {
        _requestsPerSecond = Math.Max(1, options.CurrentValue.AppDetailsRequestsPerSecond);
        _limiter = CreateLimiter(_requestsPerSecond);
        options.OnChange(Reconfigure);
    }

    public async ValueTask AcquireAsync(CancellationToken ct = default)
    {
        TokenBucketRateLimiter limiter;
        lock (_sync)
            limiter = _limiter;

        using var lease = await limiter.AcquireAsync(permitCount: 1, ct);
        if (!lease.IsAcquired)
            throw new OperationCanceledException("SteamSpy rate limiter queue overflow.", ct);
    }

    private void Reconfigure(SteamSpyImportOptions options)
    {
        var rps = Math.Max(1, options.AppDetailsRequestsPerSecond);
        lock (_sync)
        {
            if (rps == _requestsPerSecond)
                return;

            _requestsPerSecond = rps;
            _limiter.Dispose();
            _limiter = CreateLimiter(rps);
        }
    }

    private static TokenBucketRateLimiter CreateLimiter(int requestsPerSecond)
        => new(new TokenBucketRateLimiterOptions
        {
            TokenLimit           = requestsPerSecond,
            TokensPerPeriod      = requestsPerSecond,
            ReplenishmentPeriod  = TimeSpan.FromSeconds(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = 1000,
            AutoReplenishment    = true,
        });

    public void Dispose()
    {
        lock (_sync)
            _limiter.Dispose();
    }
}
