using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

// F2.1: extracted from GameRepository — single responsibility: lookup entities only
// PERF-1 fix: uses PostgreSQL fn_upsert_* — 1 round-trip instead of INSERT + SELECT
public sealed class LookupRepository(AppDbContext db) : ILookupRepository
{
    public async Task<Developer> GetOrCreateDeveloperAsync(string name, CancellationToken ct = default)
    {
        var id = await db.Database
            .SqlQuery<int>($"SELECT fn_upsert_developer({name})")
            .FirstAsync(ct);
        // No extra SELECT needed — we have the PK, reconstruct entity
        return new Developer { DeveloperId = id, Name = name };
    }

    public async Task<Publisher> GetOrCreatePublisherAsync(string name, CancellationToken ct = default)
    {
        var id = await db.Database
            .SqlQuery<int>($"SELECT fn_upsert_publisher({name})")
            .FirstAsync(ct);
        return new Publisher { PublisherId = id, Name = name };
    }

    public async Task<Genre> GetOrCreateGenreAsync(string name, CancellationToken ct = default)
    {
        var id = await db.Database
            .SqlQuery<int>($"SELECT fn_upsert_genre({name})")
            .FirstAsync(ct);
        return new Genre { GenreId = id, Name = name };
    }
}