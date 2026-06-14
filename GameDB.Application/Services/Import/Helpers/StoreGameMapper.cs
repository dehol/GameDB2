using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Застосовує деталі з магазину до доменної сутності Game.
///
/// FIX N+1: GetOrCreateGenreAsync/TagAsync викликались по одному на кожен елемент.
/// Для гри з 10 жанрами і 20 тегами = 30 окремих SQL запитів.
/// Тепер: GetOrCreateGenresBulkAsync/GetOrCreateTagsBulkAsync — 1 INSERT + 1 SELECT
/// для всього списку жанрів/тегів незалежно від їхньої кількості.
/// </summary>
public sealed class StoreGameMapper
{
    public async Task ApplyAsync(Game game, StoreGameDetails details, IGameRepository games, bool overwriteExisting, CancellationToken ct = default)
    {
        if (overwriteExisting || string.IsNullOrWhiteSpace(game.Name))
            game.Name = details.Name;

        if (overwriteExisting || string.IsNullOrWhiteSpace(game.HeaderImage) || game.HeaderImage != details.HeaderImageUrl)
            game.HeaderImage = details.HeaderImageUrl;

        if (overwriteExisting || string.IsNullOrWhiteSpace(game.IconImage) || game.IconImage != details.IconImageUrl)
            game.IconImage = details.IconImageUrl;

        // Description: зберігаємо якщо ще немає або примусово перезаписуємо
        if (overwriteExisting || string.IsNullOrWhiteSpace(game.Description))
            game.Description = details.Description;

        if (details.Rating.HasValue && (overwriteExisting || game.Rating is null) || game.Rating != details.Rating)
            game.Rating = details.Rating;

        // Developer і Publisher: зазвичай 0-1 на гру — single-query OK.
        if (details.Developer is not null && (overwriteExisting || game.DeveloperId is null))
        {
            var dev = await games.GetOrCreateDeveloperAsync(details.Developer, ct);
            game.DeveloperId = dev.DeveloperId;
        }

        if (details.Publisher is not null && (overwriteExisting || game.PublisherId is null))
        {
            var pub = await games.GetOrCreatePublisherAsync(details.Publisher, ct);
            game.PublisherId = pub.PublisherId;
        }

        // Genres: bulk upsert замість N одиночних запитів
        if (overwriteExisting || game.Genres.Count == 0)
        {
            game.Genres.Clear();

            if (details.Genres.Any())
            {
                var genreMap = await games.GetOrCreateGenresBulkAsync(details.Genres, ct);
                foreach (var name in details.Genres)
                {
                    if (genreMap.TryGetValue(name, out var genre))
                        game.Genres.Add(genre);
                }
            }
        }

        // Tags: bulk upsert замість N одиночних запитів
        if (overwriteExisting || game.Tags.Count == 0)
        {
            game.Tags.Clear();

            if (details.Tags.Any())
            {
                var tagMap = await games.GetOrCreateTagsBulkAsync(details.Tags, ct);
                foreach (var name in details.Tags)
                {
                    if (tagMap.TryGetValue(name, out var tag))
                        game.Tags.Add(tag);
                }
            }
        }
    }
}