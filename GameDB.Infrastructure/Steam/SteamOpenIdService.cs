using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace GameDB.Infrastructure.Steam;

/// <summary>
/// Ручна реалізація Steam OpenID 2.0 без сторонніх пакетів.
/// Steam не підтримує OpenID Connect (OIDC), тому використовуємо «checkid_setup» flow.
/// </summary>
public class SteamOpenIdService
{
    private const string SteamOpenIdUrl = "https://steamcommunity.com/openid/login";
    private static readonly Regex SteamIdRegex =
        new(@"https://steamcommunity\.com/openid/id/(\d+)", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public SteamOpenIdService(HttpClient http)
    {
        _http = http;
    }

    // ─── Крок 1: будуємо URL для редіректу на Steam ────────────────────────
    public string BuildRedirectUrl(string returnUrl, string realm)
    {
        var p = new Dictionary<string, string>
        {
            ["openid.ns"]         = "http://specs.openid.net/auth/2.0",
            ["openid.mode"]       = "checkid_setup",
            ["openid.return_to"]  = returnUrl,
            ["openid.realm"]      = realm,
            ["openid.identity"]   = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
        };

        var qs = string.Join("&", p.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{SteamOpenIdUrl}?{qs}";
    }

    // ─── Крок 2: верифікуємо відповідь від Steam ────────────────────────────
    /// <returns>SteamID64 (рядок цифр), або null якщо верифікація провалилась.</returns>
    public async Task<string?> ValidateCallbackAsync(Dictionary<string, string> query)
    {
        if (query["openid.mode"] != "id_res")
            return null;

        var claimedId = query["openid.claimed_id"].ToString();
        var match = SteamIdRegex.Match(claimedId);
        if (!match.Success) return null;

        // Замінюємо mode на check_authentication і відправляємо назад у Steam
        var fields = query
            .Where(kv => kv.Key.StartsWith("openid."))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        fields["openid.mode"] = "check_authentication";

        var content = new FormUrlEncodedContent(fields);
        var response = await _http.PostAsync(SteamOpenIdUrl, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!body.Contains("is_valid:true"))
            return null;

        return match.Groups[1].Value; // SteamID64
    }
}
