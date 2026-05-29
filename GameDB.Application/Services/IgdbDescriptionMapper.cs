using System.Text;
using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Services;

public sealed class IgdbDescriptionMapper
{
    public void ApplyDescription(Game game, IgdbGameDto dto, bool overwriteExisting)
    {
        if (!overwriteExisting && !string.IsNullOrWhiteSpace(game.Description))
            return;

        game.Description = FormatAsSteamHtml(dto.summary, dto.storyline);
        game.UpdatedAt = DateTime.UtcNow;
    }

    public static string FormatAsSteamHtml(string? summary, string? storyline)
    {
        if (string.IsNullOrWhiteSpace(summary) && string.IsNullOrWhiteSpace(storyline))
            return string.Empty;

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            foreach (var p in summary.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                sb.Append($"<p class=\"bb_paragraph\">{p.Trim()}</p>");
        }

        if (!string.IsNullOrWhiteSpace(storyline))
        {
            sb.Append("<h2 class=\"bb_tag\">Storyline</h2>");
            foreach (var p in storyline.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                sb.Append($"<p class=\"bb_paragraph\">{p.Trim()}</p>");
        }

        return sb.ToString();
    }
}
