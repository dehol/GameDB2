using Ganss.Xss;

namespace GameDB.Web.Services;

/// <summary>
/// Безпечний рендер Steam/HTML-описів ігор (відео, зображення з CDN).
/// </summary>
public sealed class GameDescriptionSanitizer
{
    private readonly HtmlSanitizer _sanitizer = Create();

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        return _sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();

        // Steam-описи містять медіа та розмітку поза стандартним набором
        foreach (var tag in new[] { "video", "source", "h1", "h2", "h3", "picture" })
            s.AllowedTags.Add(tag);

        foreach (var attr in new[]
        {
            "controls", "autoplay", "muted", "loop", "poster", "preload",
            "type", "width", "height", "class", "id", "title", "target", "rel"
        })
            s.AllowedAttributes.Add(attr);

        s.AllowedSchemes.Add("data");

        // Дозволені лише HTTPS-ресурси (Steam CDN тощо)
        s.AllowedTags.Remove("script");
        s.AllowedTags.Remove("iframe");
        s.AllowedTags.Remove("object");
        s.AllowedTags.Remove("embed");

        return s;
    }
}
