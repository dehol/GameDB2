using GameDB.Application.Services.Import;
using Hangfire;

namespace GameDB.Application.Interfaces;

public interface IPriceSyncService
{
    PriceSyncOperationState State { get; }

    /// <summary>
    /// Запускається вручну з адмін-панелі (legacy).
    /// Один Hangfire-job для всіх провайдерів послідовно.
    /// Залишено для зворотної сумісності.
    /// </summary>
    Task RunPriceSyncJobAsync(string? providerSlug, CancellationToken ct);

    /// <summary>
    /// Запускається Hangfire для одного конкретного провайдера.
    /// AdminService ставить 3 таких job'и в чергу → виконуються паралельно.
    ///
    /// [JobDisplayName] читається Hangfire при постановці в чергу
    /// (з MethodInfo інтерфейсу) → показується в Dashboard як "Ціни: steam".
    /// {0} = перший аргумент = providerSlug.
    /// </summary>
    [JobDisplayName("Ціни: {0}")]
    Task SyncProviderAsync(string providerSlug, CancellationToken ct);
}