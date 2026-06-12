using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed partial class GameRepository
{
    /// <summary>
    /// Один INSERT ON CONFLICT DO NOTHING для всього списку жанрів,
    /// потім один SELECT — 2 запити незалежно від розміру списку.
    /// </summary>
    public async Task<Dictionary<string, Genre>> GetOrCreateGenresBulkAsync(
        IReadOnlyCollection<string> names,
        CancellationToken           ct = default)
    {
        if (names.Count == 0) return [];

        var unique = names.Where(n => !string.IsNullOrWhiteSpace(n))
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToArray();

        if (unique.Length == 0) return [];

        var placeholders = string.Join(", ", Enumerable.Range(0, unique.Length).Select(i => $"({{{i}}})"));
        var sql          = $"INSERT INTO \"Genre\" (\"Name\") VALUES {placeholders} ON CONFLICT (\"Name\") DO NOTHING";
        await db.Database.ExecuteSqlRawAsync(sql, unique.Cast<object>().ToArray(), ct);

        return await db.Set<Genre>()
            .Where(g => unique.Contains(g.Name))
            .ToDictionaryAsync(g => g.Name, ct);
    }

    /// <summary>
    /// Один INSERT ON CONFLICT DO NOTHING для всього списку тегів,
    /// потім один SELECT — 2 запити незалежно від розміру списку.
    /// </summary>
    public async Task<Dictionary<string, Tag>> GetOrCreateTagsBulkAsync(
        IReadOnlyCollection<string> names,
        CancellationToken           ct = default)
    {
        if (names.Count == 0) return [];

        var unique = names.Where(n => !string.IsNullOrWhiteSpace(n))
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToArray();

        if (unique.Length == 0) return [];

        var placeholders = string.Join(", ", Enumerable.Range(0, unique.Length).Select(i => $"({{{i}}})"));
        var sql          = $"INSERT INTO \"Tag\" (\"Name\") VALUES {placeholders} ON CONFLICT (\"Name\") DO NOTHING";
        await db.Database.ExecuteSqlRawAsync(sql, unique.Cast<object>().ToArray(), ct);

        return await db.Set<Tag>()
            .Where(t => unique.Contains(t.Name))
            .ToDictionaryAsync(t => t.Name, ct);
    }
}
