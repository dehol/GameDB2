using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace GameDB.Infrastructure.Http;

public static class StoreHttpClientExtensions
{
    /// <summary>
    /// Resilience pipeline для HTTP-клієнтів зовнішніх магазинів.
    ///
    /// Порядок шарів (Polly v8 — виконуються зверху вниз для вихідного запиту):
    ///
    ///   [0] ConcurrencyLimiter   — скільки HTTP-запитів одночасно через цей клієнт
    ///   [1] TotalRequestTimeout  — жорсткий ліміт на ВСЮ спробу включно з retry
    ///   [2] CircuitBreaker       — якщо провайдер "ліг" — не бомбардуємо його
    ///   [3] Retry на 5xx         — exponential backoff з jitter
    ///   [4] Retry на 429         — читає Retry-After хедер
    ///   [5] AttemptTimeout       — ліміт на ОДИН запит
    ///
    /// Використання:
    ///   builder.Services
    ///       .AddHttpClient&lt;SteamSpyClient&gt;()
    ///       .AddStoreProviderResiliency("steamspy", maxConcurrency: 2);
    /// </summary>
    public static IHttpClientBuilder AddStoreProviderResiliency(
        this IHttpClientBuilder builder,
        string providerName = "store",
        int maxConcurrency = 4)   // per-client override: steamspy=2, gog=2, egdata=3
    {
        // AddResilienceHandler повертає IHttpResiliencePipelineBuilder, не IHttpClientBuilder,
        // тому викликаємо як statement і повертаємо оригінальний builder окремо.
        builder.AddResilienceHandler($"{providerName}-pipeline", pipeline =>
        {
            // ── [0] Concurrency Limiter ──────────────────────────────────────
            // Обмежує скільки реальних HTTP-запитів одночасно йде через цей клієнт.
            // Замінює SemaphoreSlim у сервісному шарі — throttling тепер тут,
            // видимий при реєстрації і різний для кожного провайдера.
            //
            // Надлишкові задачі стають у чергу (QueueLimit=200), а не кидають
            // виняток одразу. RateLimiterRejectedException — лише при переповненні черги.
            //
            // Значення per-client (Program.cs):
            //   steamspy : 2  — soft limit ~4 req/s, але latency ~200ms → 2 одночасно
            //   gog      : 2  — офіційний але undocumented API
            //   egdata   : 3  — 2 HTTP-запити на гру, community API
            pipeline.AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit          = maxConcurrency,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 200,
            });

            // ── [1] Загальний таймаут ────────────────────────────────────────
            // Максимальний час на всю операцію для одного externalId:
            // 3 retry по 5xx (2+4+8 = 14s backoff) + 3 запити по 30s = ~104s → беремо 3 хвилини.
            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromMinutes(3),
                Name    = $"{providerName}-total",
            });

            // ── [2] Circuit Breaker ──────────────────────────────────────────
            // Якщо 80% запитів за 2 хвилини провалилися (мінімум 10 запитів у вибірці)
            // → розриваємо контакт на 2 хвилини → решта ігор пропускається (skipped).
            // Після паузи — один тестовий запит, якщо успішний — знову відкриваємо.
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                Name              = $"{providerName}-circuit",
                SamplingDuration  = TimeSpan.FromMinutes(2),
                MinimumThroughput = 10,
                FailureRatio      = 0.8,
                BreakDuration     = TimeSpan.FromMinutes(2),
                ShouldHandle      = static args => args.Outcome switch
                {
                    { Exception: HttpRequestException }                          => ValueTask.FromResult(true),
                    { Exception: TimeoutRejectedException }                      => ValueTask.FromResult(true),
                    { Result.StatusCode: >= HttpStatusCode.InternalServerError } => ValueTask.FromResult(true),
                    _                                                            => ValueTask.FromResult(false),
                },
                OnOpened = args =>
                {
                    // Це логується автоматично Polly з рівнем Warning,
                    // тут можна додати метрики / алерти якщо треба
                    return ValueTask.CompletedTask;
                },
            });

            // ── [3] Retry при 5xx та мережевих помилках ─────────────────────
            // Exponential backoff: 2s → 4s → 8s (+ ±20% jitter).
            // Jitter важливий: без нього всі workers одночасно ретраять → thundering herd.
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                Name             = $"{providerName}-server-retry",
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.FromSeconds(2),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                ShouldHandle     = static args => args.Outcome switch
                {
                    { Exception: HttpRequestException }                          => ValueTask.FromResult(true),
                    { Exception: TimeoutRejectedException }                      => ValueTask.FromResult(true),
                    { Result.StatusCode: >= HttpStatusCode.InternalServerError } => ValueTask.FromResult(true),
                    _                                                            => ValueTask.FromResult(false),
                },
            });

            // ── [4] Retry при 429 Too Many Requests ─────────────────────────
            // Читаємо Retry-After хедер — Steam/GOG/Epic його повертають.
            // Якщо хедера немає — чекаємо 15 секунд (фіксовано, не exponential).
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                Name             = $"{providerName}-ratelimit-retry",
                MaxRetryAttempts = 5,
                Delay            = TimeSpan.FromSeconds(15),
                BackoffType      = DelayBackoffType.Constant,
                ShouldHandle     = static args => ValueTask.FromResult(
                    args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests),
                DelayGenerator   = static args =>
                {
                    var retryAfter = args.Outcome.Result?.Headers.RetryAfter;

                    // Варіант 1: Retry-After: 30  (секунди)
                    if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
                        return ValueTask.FromResult<TimeSpan?>(delta + TimeSpan.FromSeconds(1));

                    // Варіант 2: Retry-After: Wed, 21 Oct 2025 07:28:00 GMT  (дата)
                    if (retryAfter?.Date is { } date)
                    {
                        var wait = date - DateTimeOffset.UtcNow;
                        if (wait > TimeSpan.Zero)
                            return ValueTask.FromResult<TimeSpan?>(wait + TimeSpan.FromSeconds(1));
                    }

                    // Fallback: null → Polly використовує Delay (15s) з вище
                    return ValueTask.FromResult<TimeSpan?>(null);
                },
            });

            // ── [5] Таймаут на ОДИН запит ────────────────────────────────────
            // 30 секунд достатньо для будь-якого нормального price endpoint.
            // Якщо перевищено → TimeoutRejectedException → retry [3] або TotalTimeout [1].
            // БУЛО: 120s — занадто довго, заморожує worker на весь цей час.
            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                Name    = $"{providerName}-attempt",
            });
        });

        return builder;
    }
}