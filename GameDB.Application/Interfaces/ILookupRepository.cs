using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface ILookupRepository
{
    Task<Developer> GetOrCreateDeveloperAsync(string name, CancellationToken ct = default);
    Task<Publisher> GetOrCreatePublisherAsync(string name, CancellationToken ct = default);
    Task<Genre> GetOrCreateGenreAsync(string name, CancellationToken ct = default);
    Task<Tag> GetOrCreateTagAsync(string name, CancellationToken ct = default);
}
