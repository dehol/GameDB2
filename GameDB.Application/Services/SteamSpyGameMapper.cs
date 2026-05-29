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

        if (!string.IsNullOrWhiteSpace(dto.Developer) && !string.Equals(dto.Developer, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (overwriteExisting || game.DeveloperId == null)
            {
                var dev = await games.GetOrCreateDeveloperAsync(dto.Developer.Trim());
                game.DeveloperId = dev.DeveloperId;
                updated = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Publisher) && !string.Equals(dto.Publisher, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (overwriteExisting || game.PublisherId == null)
            {
                var pub = await games.GetOrCreatePublisherAsync(dto.Publisher.Trim());
                game.PublisherId = pub.PublisherId;
                updated = true;
            }
        }

        if (overwriteExisting || game.Genres.Count == 0)
        {
            var hadGenres = game.Genres.Count > 0;
            game.Genres.Clear();
            if (!string.IsNullOrWhiteSpace(dto.Genre))
            {
                foreach (var part in dto.Genre.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    var genre = await games.GetOrCreateGenreAsync(part);
                    game.Genres.Add(genre);
                }
            }
            if (hadGenres || game.Genres.Count > 0)
                updated = true;
        }

        if (overwriteExisting || game.Tags.Count == 0)
        {
            var hadTags = game.Tags.Count > 0;
            game.Tags.Clear();
            if (dto.Tags is { Count: > 0 })
            {
                var topTags = dto.Tags
                    .OrderByDescending(kv => kv.Value)
                    .Take(_options.MaxTagsPerGame)
                    .Select(kv => kv.Key);

                foreach (var tagName in topTags)
                {
                    if (string.IsNullOrWhiteSpace(tagName)) continue;
                    var tag = await games.GetOrCreateTagAsync(tagName.Trim());
                    game.Tags.Add(tag);
                }
            }
            if (hadTags || game.Tags.Count > 0)
                updated = true;
        }

        var totalReviews = dto.Positive + dto.Negative;
        if (totalReviews > 0 && (overwriteExisting || game.Rating == null || game.RatingCount == null))
        {
            game.Rating = Math.Round(dto.Positive * 100.0 / totalReviews, 1);
            game.RatingCount = totalReviews;
            updated = true;
        }

        if (updated)
            game.UpdatedAt = DateTime.UtcNow;
    }
}
