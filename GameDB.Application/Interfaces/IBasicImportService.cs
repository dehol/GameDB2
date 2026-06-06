using GameDB.Application.Services.Import;

namespace GameDB.Application.Interfaces;

public interface IBasicImportService
{
    BasicImportOperationState State { get; }
    
    Task RunImportJobAsync(string? providerSlug, CancellationToken ct);
}