using GameDB.Application.Constants;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Application.Services;
using GameDB.Application.Services.Import;
using Microsoft.Extensions.Options;

namespace GameDB.Infrastructure.Providers;

public sealed class SteamStoreProvider(
    ISteamSpyClient spyClient,
    ISteamClient steamClient,
    SteamGameFilter filter,
    IOptions<SteamSpyImportOptions> options) : IStoreProvider
{
    private readonly SteamSpyImportOptions _opts = options.Value;

    public int    ShopId                 => ShopIds.Steam;
    public string Slug                   => "steam";
    public int    DelayBetweenRequestsMs => _opts.DelayBetweenRequestsMs;

    public async Task<IReadOnlyCollection<StoreGameListItem>> GetGameListAsync(CancellationToken ct)
    {
        var raw = await steamClient.GetAppListAsync(ct);
        return raw
            .Where(r => r.Appid > 0 && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new StoreGameListItem(r.Appid.ToString(), r.Name!))
            .ToList();
    }

    public bool IsValidItem(StoreGameListItem item)
        => int.TryParse(item.ExternalId, out _) && filter.IsValidName(item.Name);

    public async Task<StoreGameDetails?> GetGameDetailsAsync(string externalId, CancellationToken ct)
    {
        if (!int.TryParse(externalId, out var appId)) return null;
        var dto = await spyClient.GetAppDetailsAsync(appId, ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Name)
            || dto.Name.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        var totalReviews = dto.Positive + dto.Negative;
        double? rating = totalReviews > 0
            ? Math.Round(dto.Positive * 100.0 / totalReviews, 1)
            : null;

        var tags = dto.Tags?
            .OrderByDescending(kv => kv.Value)
            .Take(_opts.MaxTagsPerGame)
            .Select(kv => kv.Key)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray() ?? [];

        var genres = dto.Genre?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .ToArray() ?? [];

        return new StoreGameDetails
        {
            ExternalId     = externalId,
            Name           = dto.Name,
            Developer      = NoneToNull(dto.Developer),
            Publisher      = NoneToNull(dto.Publisher),
            Genres         = genres,
            Tags           = tags,
            Rating         = rating,
            RatingCount    = totalReviews > 0 ? totalReviews : null,
            HeaderImageUrl = BuildHeaderImageUrl(appId),
            IconImageUrl   = BuildIconImageUrl(appId),
            StoreUrl       = BuildStoreUrl(appId)
        };
    }

    public async Task<StorePriceInfo?> GetPriceAsync(string externalId, CancellationToken ct)
    {
        if (!int.TryParse(externalId, out var appId)) return null;
        var dto = await spyClient.GetAppDetailsAsync(appId, ct);
        if (dto is null) return null;
        if (!TryParsePrice(dto.InitialPrice ?? dto.Price, out var price)) return null;
        short.TryParse(dto.Discount, out var discount);
        return new StorePriceInfo(price, discount, "USD", BuildStoreUrl(appId));
    }

    /// <summary>Steam: будує URL по числовому AppId. Slug ігнорується.</summary>
    public string? BuildExternalUrl(string externalId, string? slug = null)
        => int.TryParse(externalId, out var appId) ? BuildStoreUrl(appId) : null;

    public static string BuildHeaderImageUrl(int appId)
        => $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
    public static string BuildIconImageUrl(int appId)
        => $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/capsule_184x69.jpg";
    public static string BuildStoreUrl(int appId)
        => $"https://store.steampowered.com/app/{appId}/";

    private static string? NoneToNull(string? s)
        => string.IsNullOrWhiteSpace(s) || s.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : s;

    private static bool TryParsePrice(string? priceCents, out decimal price)
    {
        price = 0;
        if (!int.TryParse(priceCents, out var cents)) return false;
        price = cents / 100m;
        return true;
    }
}
