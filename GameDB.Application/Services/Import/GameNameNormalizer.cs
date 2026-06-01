using System.Text.RegularExpressions;

namespace GameDB.Application.Services.Import;

public static class GameNameNormalizer
{
    // Видаляємо торгові марки
    private static readonly Regex Trademarks = 
        new(@"[®™©]", RegexOptions.Compiled);

    // Видаляємо маркери видань (можна розширювати словник)
    private static readonly Regex EditionFluff = 
        new(@"\b(deluxe|standard|gold|ultimate|goty|edition|directors cut|remastered|enhanced|pc edition)\b", 
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Замінюємо знаки розв'язки (двокрапки, дефіси, слеші) на пробіли
    private static readonly Regex Delimiters = 
        new(@"[\-_:,\|\/]+", RegexOptions.Compiled);

    // Сплющуємо множинні пробіли
    private static readonly Regex ExcessWhitespace = 
        new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        string result = name.ToLowerInvariant();

        result = Trademarks.Replace(result, "");
        result = EditionFluff.Replace(result, " ");
        result = Delimiters.Replace(result, " ");
        
        // Розумна заміна римських цифр для ігрових сіквелів
        result = ReplaceRomanNumerals(result);

        // Очищаємо пробіли, але зберігаємо символи типу '!' чи '?'
        result = ExcessWhitespace.Replace(result, " ").Trim();

        return result;
    }

    private static string ReplaceRomanNumerals(string text)
    {
        return text
            .Replace(" ii ", " 2 ").Replace(" ii", " 2")
            .Replace(" iii ", " 3 ").Replace(" iii", " 3")
            .Replace(" iv ", " 4 ").Replace(" iv", " 4")
            .Replace(" v ", " 5 ").Replace(" v", " 5")
            .Replace(" vi ", " 6 ").Replace(" vi", " 6");
    }
}