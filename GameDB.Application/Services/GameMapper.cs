using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

public class GameMapper
{
    public void ApplyDetails(Game game, SteamAppDetailsData details)
    {
        game.Description = details.detailed_description ?? details.short_description;
        game.HeaderImage = details.header_image;
        game.IsFree = details.is_free;
        game.IconImage = details.capsule_imagev5 ?? details.capsule_image;
        game.UpdatedAt = DateTime.UtcNow;

        if (details.metacritic != null)
            game.Rating = details.metacritic.score;

        if (details.recommendations != null)
            game.RatingCount = details.recommendations.total;

        if (details.release_date != null && !string.IsNullOrEmpty(details.release_date.date))
        {
            if (DateOnly.TryParse(details.release_date.date, out var parsedDate))
            {
                game.ReleaseDate = parsedDate;
            }
        }
    }
}