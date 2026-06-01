using System.Text.RegularExpressions;

namespace GameDB.Application.Services.Import;

public static class GameNameNormalizer
{
    private static readonly Regex SpecialChars =
        new(@"[®™©:–—''""\.!\?,]", RegexOptions.Compiled);

    private static readonly Regex Whitespace =
        new(@"[\s\-_]+", RegexOptions.Compiled);

    public static string Normalize(string name)
        => Whitespace
            .Replace(SpecialChars.Replace(name, ""), " ")
            .Trim()
            .ToLowerInvariant();
}
