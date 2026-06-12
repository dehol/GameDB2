using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Domain.Entities;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.AI;

/// <summary>
/// Обчислює user embedding як усереднений L2-нормований вектор
/// по всіх іграх бібліотеки користувача.
///
/// Алгоритм:
///   1. Беремо всі owned games
///   2. Завантажуємо їхні pgvector embeddings
///   3. Компонентно сумуємо → ділимо на кількість
///   4. L2-нормалізуємо результат (unit vector)
/// </summary>
public sealed class UserEmbeddingService(
    AppDbContext                db,
    IUserCollectionRepository   collections,
    ILogger<UserEmbeddingService> logger) : IUserEmbeddingService
{
    public async Task<float[]?> ComputeUserEmbeddingAsync(int userId, CancellationToken ct)
    {
        // ── Крок 1: owned game IDs ────────────────────────────────────────────
        var library  = await collections.GetLibraryAsync(userId, ct);
        var ownedIds = library.Select(g => g.GameId).Distinct().ToList();

        if (ownedIds.Count == 0)
        {
            logger.LogInformation("UserEmbedding: userId={UserId} — бібліотека порожня", userId);
            return null;
        }

        // ── Крок 2: embeddings із БД ──────────────────────────────────────────
        var vectors = await db.Set<Game>()
            .Where(g => ownedIds.Contains(g.GameId) && g.Embedding != null)
            .Select(g => g.Embedding!)
            .AsNoTracking()
            .ToListAsync(ct);

        if (vectors.Count == 0)
        {
            logger.LogWarning(
                "UserEmbedding: userId={UserId} — {Count} ігор знайдено, але жодна не має embedding",
                userId, ownedIds.Count);
            return null;
        }

        logger.LogInformation(
            "UserEmbedding: userId={UserId} — усереднюємо {Count} векторів",
            userId, vectors.Count);

        // ── Крок 3: компонентне усереднення ──────────────────────────────────
        var first = vectors[0].ToArray();
        int dim   = first.Length;
        var sum   = new double[dim];

        foreach (var vec in vectors)
        {
            var arr = vec.ToArray();
            for (int i = 0; i < dim; i++)
                sum[i] += arr[i];
        }

        var avg = new float[dim];
        for (int i = 0; i < dim; i++)
            avg[i] = (float)(sum[i] / vectors.Count);

        // ── Крок 4: L2-нормалізація → unit vector ────────────────────────────
        double norm = Math.Sqrt(avg.Sum(x => (double)x * x));
        if (norm > 1e-10)
            for (int i = 0; i < dim; i++)
                avg[i] = (float)(avg[i] / norm);

        logger.LogDebug(
            "UserEmbedding: userId={UserId} — вектор dim={Dim}, norm={Norm:F4}",
            userId, dim, norm);

        return avg;
    }
}
