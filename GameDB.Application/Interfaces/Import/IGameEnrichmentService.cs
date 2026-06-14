using GameDB.Application.Services.Import;
using Hangfire;

namespace GameDB.Application.Interfaces;

public interface IGameEnrichmentService
{
    EnrichmentOperationState State { get; }

    /// <summary>
    /// Основний шлях: один провайдер, запускається Hangfire окремим job'ом.
    /// AdminService ставить 3 таких job'и → виконуються паралельно.
    /// [JobDisplayName] читається з MethodInfo інтерфейсу при Enqueue.
    /// </summary>
    [JobDisplayName("Збагачення: {0}")]
    Task EnrichProviderAsync(string providerSlug, bool overwriteExisting, CancellationToken ct);

}