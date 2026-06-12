using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Services;
using GameDB.Domain.Entities;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.AI;

/// <summary>
/// Будує повний профіль вподобань користувача на основі бібліотеки.
/// Не зберігається в БД — обчислюється на льоту.
/// </summary>
public sealed class UserProfileService(
    AppDbContext                 db,
    IUserCollectionRepository    collections,
    IUserEmbeddingService        embeddingService,
    ILogger<UserProfileService>  logger) : IUserProfileService
{
    // Скільки топ-жанрів/тегів повертати
    private const int TopGenresCount   = 10;
    private const int TopTagsCount     = 20;
    private const int TopFeaturesCount = 10;

    public async Task<UserPreferenceProfileDto> GetProfileAsync(int userId, CancellationToken ct)
    {
        logger.LogInformation("UserProfile: завантажуємо профіль для userId={UserId}", userId);

        // ── Бібліотека та вішліст ─────────────────────────────────────────────
        var library  = await collections.GetLibraryAsync(userId, ct);
        var wishlist = await collections.GetWishlistAsync(userId, ct);

        var ownedIds    = library.Select(g => g.GameId).Distinct().ToList();
        var wishlistIds = wishlist.Select(g => g.GameId).Distinct().ToList();

        // ── Ігри бібліотеки з жанрами та тегами ──────────────────────────────
        var ownedGames = await db.Set<Game>()
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .Where(g => ownedIds.Contains(g.GameId))
            .AsNoTracking()
            .ToListAsync(ct);

        // ── Частота жанрів (топ-10) ───────────────────────────────────────────
        var favoriteGenres = ownedGames
            .SelectMany(g => g.Genres.Select(gg => gg.Name))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(TopGenresCount)
            .Select(g => g.Key)
            .ToList();

        // ── Частота тегів (топ-20) ────────────────────────────────────────────
        var favoriteTags = ownedGames
            .SelectMany(g => g.Tags.Select(t => t.Name))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(TopTagsCount)
            .Select(g => g.Key)
            .ToList();

        // ── Топ features: теги мінус базові жанри ────────────────────────────
        var genreNamesLower = favoriteGenres
            .Select(g => g.ToLowerInvariant())
            .ToHashSet();

        var topFeatures = favoriteTags
            .Where(t => !genreNamesLower.Contains(t.ToLowerInvariant()))
            .Take(TopFeaturesCount)
            .ToList();

        // ── User embedding ────────────────────────────────────────────────────
        var embedding = await embeddingService.ComputeUserEmbeddingAsync(userId, ct);

        logger.LogInformation(
            "UserProfile: userId={UserId} — {OwnedCount} owned, {WishCount} wishlist, " +
            "{GenreCount} genres, {TagCount} tags, embedding={HasEmb}",
            userId, ownedIds.Count, wishlistIds.Count,
            favoriteGenres.Count, favoriteTags.Count,
            embedding != null ? $"dim={embedding.Length}" : "null");

        return new UserPreferenceProfileDto
        {
            UserId          = userId,
            FavoriteGenres  = favoriteGenres,
            FavoriteTags    = favoriteTags,
            TopFeatures     = topFeatures,
            OwnedGameIds    = ownedIds,
            WishlistGameIds = wishlistIds,
            UserEmbedding   = embedding?.ToList() ?? [],
        };
    }
}
