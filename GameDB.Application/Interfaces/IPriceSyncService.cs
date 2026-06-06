using GameDB.Application.Services.Import;

namespace GameDB.Application.Interfaces;

public interface IPriceSyncService
{
    PriceSyncOperationState State { get; }
    
    Task RunPriceSyncJobAsync(string? providerSlug, CancellationToken ct);
}