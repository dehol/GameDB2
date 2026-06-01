using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services.Import;

public sealed class StoreGameMapper
{
    public async Task ApplyAsync(
        Game game,
        StoreGameDetails details,
        IGameRepository games,
        bool overwriteExisting,
        CancellationToken ct = default)
    {
        if (overwriteExisting || string.IsNullOrWhiteSpace(game.Name))
            game.Name = details.Name;

        if (overwriteExisting || string.IsNullOrWhiteSpace(game.HeaderImage))
            game.HeaderImage = details.HeaderImageUrl;

        if (overwriteExisting || string.IsNullOrWhiteSpace(game.IconImage))
            game.IconImage = details.IconImageUrl;
            
        if (details.Rating.HasValue && (overwriteExisting || game.Rating == null))
            game.Rating = details.Rating;

        if (details.Developer is not null && (overwriteExisting || game.DeveloperId == null))
        {
            var dev = await games.GetOrCreateDeveloperAsync(details.Developer, ct);
            game.DeveloperId = dev.DeveloperId;
        }

        if (details.Publisher is not null && (overwriteExisting || game.PublisherId == null))
        {
            var pub = await games.GetOrCreatePublisherAsync(details.Publisher, ct);
            game.PublisherId = pub.PublisherId;
        }

        if (overwriteExisting || game.Genres.Count == 0)
        {
            game.Genres.Clear();
            foreach (var g in details.Genres)
            {
                var genre = await games.GetOrCreateGenreAsync(g, ct);
                game.Genres.Add(genre);
            }
        }

        if (overwriteExisting || game.Tags.Count == 0)
        {
            game.Tags.Clear();
            foreach (var t in details.Tags)
            {
                var tag = await games.GetOrCreateTagAsync(t, ct);
                game.Tags.Add(tag);
            }
        }
    }
}
