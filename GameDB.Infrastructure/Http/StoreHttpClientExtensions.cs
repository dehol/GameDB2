using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace GameDB.Infrastructure.Http;

public static class StoreHttpClientExtensions
{
    /// <summary>
    /// Стандартний Resilience pipeline для всіх клієнтів зовнішніх магазинів:
    /// retry на 5xx/мережу, окремий retry на 429, timeout 30 сек.
    /// </summary>
    public static IHttpClientBuilder AddStoreProviderResiliency(
        this IHttpClientBuilder builder,
        string handlerName = "store-provider")
    {
        builder.AddResilienceHandler(handlerName, pipeline =>
        {
            // 1. Retry при мережевих помилках і 5xx
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.FromSeconds(2),
                BackoffType      = DelayBackoffType.Exponential,   // 2s → 4s → 8s
                ShouldHandle     = args => args.Outcome switch
                {
                    { Exception: HttpRequestException }                          => ValueTask.FromResult(true),
                    { Result.StatusCode: >= HttpStatusCode.InternalServerError } => ValueTask.FromResult(true),
                    _                                                            => ValueTask.FromResult(false)
                }
            });

            // 2. Окремий retry при 429 Too Many Requests
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay            = TimeSpan.FromSeconds(10),
                BackoffType      = DelayBackoffType.Constant,
                ShouldHandle     = args => ValueTask.FromResult(
                    args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
            });

            // 3. Hard timeout на один запит
            pipeline.AddTimeout(TimeSpan.FromSeconds(120));
        });

        return builder; // ✅ повертаємо оригінальний IHttpClientBuilder
    }
}