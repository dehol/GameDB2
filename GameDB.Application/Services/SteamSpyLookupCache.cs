using System.Collections.Concurrent;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

/// <summary>
/// Session-scoped in-memory cache for lookup upserts during a full enrichment run.
/// </summary>
public sealed class SteamSpyLookupCache : IDisposable
{
    private readonly ILookupRepository _lookups;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    private readonly ConcurrentDictionary<string, Developer> _developers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Publisher> _publishers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Genre> _genres = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Tag> _tags = new(StringComparer.OrdinalIgnoreCase);

    public SteamSpyLookupCache(ILookupRepository lookups)
    {
        _lookups = lookups;
    }

    public async Task<Developer> GetOrCreateDeveloperAsync(string name, CancellationToken ct = default)
    {
        if (_developers.TryGetValue(name, out var cached))
            return cached;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_developers.TryGetValue(name, out cached))
                return cached;

            var entity = await _lookups.GetOrCreateDeveloperAsync(name, ct);
            _developers[name] = entity;
            return entity;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Publisher> GetOrCreatePublisherAsync(string name, CancellationToken ct = default)
    {
        if (_publishers.TryGetValue(name, out var cached))
            return cached;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_publishers.TryGetValue(name, out cached))
                return cached;

            var entity = await _lookups.GetOrCreatePublisherAsync(name, ct);
            _publishers[name] = entity;
            return entity;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Genre> GetOrCreateGenreAsync(string name, CancellationToken ct = default)
    {
        if (_genres.TryGetValue(name, out var cached))
            return cached;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_genres.TryGetValue(name, out cached))
                return cached;

            var entity = await _lookups.GetOrCreateGenreAsync(name, ct);
            _genres[name] = entity;
            return entity;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Tag> GetOrCreateTagAsync(string name, CancellationToken ct = default)
    {
        if (_tags.TryGetValue(name, out var cached))
            return cached;

        await _semaphore.WaitAsync(ct);
        try
        {
            if (_tags.TryGetValue(name, out cached))
                return cached;

            var entity = await _lookups.GetOrCreateTagAsync(name, ct);
            _tags[name] = entity;
            return entity;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
