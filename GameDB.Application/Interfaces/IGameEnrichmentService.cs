using GameDB.Application.Services.Import;

namespace GameDB.Application.Interfaces;

public interface IGameEnrichmentService
{
    EnrichmentOperationState State { get; }
    
    Task RunEnrichmentJobAsync(string? providerSlug, bool overwriteExisting, CancellationToken ct);
}