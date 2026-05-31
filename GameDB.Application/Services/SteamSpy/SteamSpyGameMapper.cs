using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Domain.Entities;
using Microsoft.Extensions.Options;

namespace GameDB.Application.Services;

public sealed class SteamSpyGameMapper(IOptions<SteamSpyImportOptions> options)
{
    private readonly SteamSpyImportOptions _options = options.Value;

    public async Task ApplyAsync(
        Game game,
        SteamSpyAppDetailsDto dto,
        IGameRepository games,
        bool overwriteExisting,
        CancellationToken ct = default)
    {
        var updated = false;

        // ── Developer ────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(dto.Developer)
            && !string.Equals(dto.Developer, "none", StringComparison.OrdinalIgnoreCase)
            && (overwriteExisting || game.DeveloperId is null))
        {
            var dev = await games.GetOrCreateDeveloperAsync(dto.Developer.Trim());
            game.DeveloperId = dev.DeveloperId;
            updated = true;
        }

        // ── Publisher ────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(dto.Publisher)
            && !string.Equals(dto.Publisher, "none", StringComparison.OrdinalIgnoreCase)
            && (overwriteExisting || game.PublisherId is null))
        {
            var pub = await games.GetOrCreatePublisherAsync(dto.Publisher.Trim());
            game.PublisherId = pub.PublisherId;
            updated = true;
        }

        // ── Genres ───────────────────────────────────────────────────────────
        if (overwriteExisting || game.Genres == null)
        {
            var hadGenres = game.Genres.Count > 0;
            game.Genres.Clear();
            if (!string.IsNullOrWhiteSpace(dto.Genre))
            {
                foreach (var part in dto.Genre.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    var genre = await games.GetOrCreateGenreAsync(part);
                    game.Genres.Add(genre);
                }
            }
            if (hadGenres || game.Genres.Count > 0) updated = true;
        }

        // ── Tags ─────────────────────────────────────────────────────────────
        if (overwriteExisting || game.Tags == null)
        {
            var hadTags = game.Tags.Count > 0;
            game.Tags.Clear();
            if (dto.Tags is { Count: > 0 })
            {
                foreach (var tagName in dto.Tags
                    .OrderByDescending(kv => kv.Value)
                    .Take(_options.MaxTagsPerGame)
                    .Select(kv => kv.Key))
                {
                    if (string.IsNullOrWhiteSpace(tagName)) continue;
                    var tag = await games.GetOrCreateTagAsync(tagName.Trim());
                    game.Tags.Add(tag);
                }
            }
            if (hadTags || game.Tags.Count > 0) updated = true;
        }

        // ── Rating ───────────────────────────────────────────────────────────
        var totalReviews = dto.Positive + dto.Negative;
        if (totalReviews > 0 && (overwriteExisting || game.Rating is null || game.RatingCount is null))
        {
            game.Rating      = Math.Round(dto.Positive * 100.0 / totalReviews, 1);
            game.RatingCount = totalReviews;
            updated = true;
        }

        // ── Images (відновлення якщо втрачені або overwrite) ─────────────────
        if (overwriteExisting || game.HeaderImage is null || game.IconImage is null)
        {
            var steamExt = game.ExternalIds
                .FirstOrDefault(e => e.ShopId == SteamSpyImportService.SteamShopId);

            if (steamExt is not null && int.TryParse(steamExt.ExternalId, out var appId))
            {
                if (overwriteExisting || game.HeaderImage is null)
                {
                    game.HeaderImage = SteamSpyImportService.BuildHeaderImageUrl(appId);
                    updated = true;
                }
                if (overwriteExisting || game.IconImage is null)
                {
                    game.IconImage = SteamSpyImportService.BuildIconImageUrl(appId);
                    updated = true;
                }
            }
        }

        if (updated)
            game.UpdatedAt = DateTime.UtcNow;
    }
}