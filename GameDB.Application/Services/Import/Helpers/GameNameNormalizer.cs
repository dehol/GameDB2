using System.Text.RegularExpressions;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Нормалізує назву гри для cross-store матчингу.
///
/// FIX 1: Апострофи. "Don't Starve" і "Dont Starve" (GOG-формат) тепер обидва → "dont starve".
///         Раніше: "don't starve" ≠ "dont starve" → матч провалювався.
///
/// FIX 2: Римські цифри. Наївний Replace(" v ", " 5 ") замінював "v" в кінці слів
///         ("The Brave" містить "e b-r-a-v-e" — не торкаємось) і не розрізняв пробіли.
///         Тепер: Regex з \b (word boundary) — тільки самостійне слово.
///         "Fallout IV" → "fallout 4", але "Vivaldi" залишається "vivaldi".
/// </summary>
public static partial class GameNameNormalizer
{
    // Торгові марки — видаляємо
    private static readonly Regex Trademarks =
        new(@"[®™©]", RegexOptions.Compiled);

    // Розділювачі → пробіл
    private static readonly Regex Delimiters =
        new(@"[\-_:,|\/]+", RegexOptions.Compiled);

    // Зайві пробіли
    private static readonly Regex ExcessWhitespace =
        new(@"\s+", RegexOptions.Compiled);

    // Апострофи (straight ' та curly ' \u2019) — прибираємо для нормалізованого порівняння
    private static readonly Regex Apostrophes =
        new(@"['\u2019]", RegexOptions.Compiled);

    // Римські цифри — лише як самостійне слово (\b), щоб уникнути помилкової заміни
    // у словах: "vivaldi" (vi), "arrive" (iv), "active" (iv), "brave" (v) тощо.
    private static readonly Regex Roman2  = new(@"\bii\b",   RegexOptions.Compiled);
    private static readonly Regex Roman3  = new(@"\biii\b",  RegexOptions.Compiled);
    private static readonly Regex Roman4  = new(@"\biv\b",   RegexOptions.Compiled);
    private static readonly Regex Roman5  = new(@"\bv\b",    RegexOptions.Compiled);
    private static readonly Regex Roman6  = new(@"\bvi\b",   RegexOptions.Compiled);
    private static readonly Regex Roman7  = new(@"\bvii\b",  RegexOptions.Compiled);
    private static readonly Regex Roman8  = new(@"\bviii\b", RegexOptions.Compiled);
    private static readonly Regex Roman9  = new(@"\bix\b",   RegexOptions.Compiled);
    private static readonly Regex Roman10 = new(@"\bx\b",    RegexOptions.Compiled);

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // 1. До нижнього регістру
        string result = name.ToLowerInvariant();

        // 2. Торгові марки
        result = Trademarks.Replace(result, "");

        // 4. Апострофи — прибираємо (don't → dont, can't → cant)
        result = Apostrophes.Replace(result, "");

        // 5. Розділювачі → пробіл
        result = Delimiters.Replace(result, " ");

        // 6. Римські цифри (порядок важливий: довші шаблони першими)
        result = Roman8.Replace(result, "8");
        result = Roman7.Replace(result, "7");
        result = Roman3.Replace(result, "3");  // iii перед ii
        result = Roman2.Replace(result, "2");
        result = Roman9.Replace(result, "9");  // ix перед i окремо (немає ix-ного слова тут)
        result = Roman6.Replace(result, "6");  // vi перед v
        result = Roman5.Replace(result, "5");
        result = Roman4.Replace(result, "4");  // iv перед i
        result = Roman10.Replace(result, "10");

        // 7. Зайві пробіли
        result = ExcessWhitespace.Replace(result, " ").Trim();

        return result;
    }
}
