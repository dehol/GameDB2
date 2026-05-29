using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

/// <summary>
/// In-memory cache for GetOrCreate lookups within a single import batch.
/// </summary>
public sealed class SteamSpyLookupCache(IGameRepository games)
{
    private readonly Dictionary<string, Developer> _developers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Publisher> _publishers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Genre> _genres = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Tag> _tags = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Developer> GetOrCreateDeveloperAsync(string name)
    {
        if (_developers.TryGetValue(name, out var cached))
            return cached;

        var entity = await games.GetOrCreateDeveloperAsync(name);
        _developers[name] = entity;
        return entity;
    }

    public async Task<Publisher> GetOrCreatePublisherAsync(string name)
    {
        if (_publishers.TryGetValue(name, out var cached))
            return cached;

        var entity = await games.GetOrCreatePublisherAsync(name);
        _publishers[name] = entity;
        return entity;
    }

    public async Task<Genre> GetOrCreateGenreAsync(string name)
    {
        if (_genres.TryGetValue(name, out var cached))
            return cached;

        var entity = await games.GetOrCreateGenreAsync(name);
        _genres[name] = entity;
        return entity;
    }

    public async Task<Tag> GetOrCreateTagAsync(string name)
    {
        if (_tags.TryGetValue(name, out var cached))
            return cached;

        var entity = await games.GetOrCreateTagAsync(name);
        _tags[name] = entity;
        return entity;
    }
}
