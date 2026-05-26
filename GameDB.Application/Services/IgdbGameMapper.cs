using System.Text;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

// F2.2: extracted from FastIgdbImportService — single responsibility: mapping only
public sealed class IgdbGameMapper
{
    public void ApplyIgdbData(Game game, IgdbGameDto dto)
    {
        game.Description = FormatDescription(dto.summary, dto.storyline);

        if (dto.cover?.image_id is { } imageId)
        {
            game.HeaderImage = $"https://images.igdb.com/igdb/image/upload/t_1080p/{imageId}.jpg";
            game.IconImage   = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
        }

        if (dto.rating.HasValue)
            game.Rating = Math.Round(dto.rating.Value, 1);

        if (dto.rating_count.HasValue)
            game.RatingCount = dto.rating_count.Value;

        if (dto.first_release_date.HasValue)
            game.ReleaseDate = DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeSeconds(dto.first_release_date.Value).UtcDateTime);

        game.UpdatedAt = DateTime.UtcNow;
    }

    private static string FormatDescription(string? summary, string? storyline)
    {
        if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(storyline))
            return string.Empty;

        var sb = new StringBuilder();
        AppendParagraphs(sb, summary);

        if (!string.IsNullOrWhiteSpace(storyline))
        {
            sb.Append("<h2 class=\"bb_tag\">Storyline</h2>");
            AppendParagraphs(sb, storyline);
        }

        return sb.ToString();
    }

    private static void AppendParagraphs(StringBuilder sb, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            sb.Append($"<p class=\"bb_paragraph\">{line.Trim()}</p>");
    }
}