using GameDB.Application.Constants;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using Microsoft.Extensions.Options;

namespace GameDB.Infrastructure.Providers;

public sealed class GogStoreProvider(
    IGogClient client,
    IOptions<GogImportOptions> options) : IStoreProvider
{
    private readonly GogImportOptions _opts = options.Value;

    public int    ShopId                 => ShopIds.Gog;
    public string Slug                   => "gog";
    public int    DelayBetweenRequestsMs => _opts.DelayBetweenRequestsMs;
    private static int _totalPage = 251;

    public static int TotalPage
    {
        get => _totalPage;
        set => _totalPage = Math.Max(_totalPage, value);
    }
    public async Task<IReadOnlyCollection<StoreGameListItem>> GetGameListAsync(CancellationToken ct)
    {
        var result = new List<StoreGameListItem>();
        int page = 1;
    
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var dto = await client.GetGamesPageAsync(page, ct);
            //TotalPage = dto.TotalPages;
            // FIX: null + порожній список → зупинка
            if (dto is null || dto.Products.Count == 0) 
            {
                page++;
                continue;
            }
            foreach (var p in dto.Products)
                if (!string.IsNullOrWhiteSpace(p.Title))
                    // FIX: Slug доступний у списку — зберігаємо для BuildExternalUrl
                    result.Add(new StoreGameListItem(p.Id.ToString(), p.Title, p.Slug));

            if (page >= TotalPage) break;
            page++;
            await Task.Delay(_opts.DelayBetweenRequestsMs, ct);
        }

        return result;
    }

    public bool IsValidItem(StoreGameListItem item)
        => !string.IsNullOrWhiteSpace(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Name);

    public async Task<StoreGameDetails?> GetGameDetailsAsync(string externalId, CancellationToken ct)
    {
        var dto = await client.GetProductDetailsAsync(externalId, ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Title)) return null;

        // GOG rating: шкала 0–50 → нормалізуємо до 0–100
        double? rating = dto.Rating > 0 ? Math.Round(dto.Rating * 2.0, 1) : null;

        var genres = dto.Genres
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => g.Name!).ToArray();

        var tags = dto.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => t.Name!).Take(20).ToArray();

        return new StoreGameDetails
        {
            ExternalId     = externalId,
            Name           = dto.Title,
            Developer      = NullIfEmpty(dto.Developer),
            Publisher      = NullIfEmpty(dto.Publisher),
            Genres         = genres,
            Tags           = tags,
            Rating         = rating,
            RatingCount    = null,
            HeaderImageUrl = NormalizeUrl(dto.Images?.Background),
            IconImageUrl   = NormalizeUrl(dto.Images?.Logo),
            // FIX: slug із Details API точніший, ніж зі списку
            StoreUrl       = BuildStoreUrl(dto.Slug ?? externalId)
        };
    }

    public async Task<StorePriceInfo?> GetPriceAsync(string externalId, CancellationToken ct)
    {
        var dto = await client.GetProductDetailsAsync(externalId, ct);
        if (dto?.Price is null) return null;

        if (dto.Price.IsFree)
            return new StorePriceInfo(0m, 0, "USD", BuildStoreUrl(dto.Slug ?? externalId));

        if (!decimal.TryParse(dto.Price.Amount,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var price))
            return null;

        var discount = (short)Math.Clamp(dto.Price.DiscountPercentage, 0, 100);
        return new StorePriceInfo(price, discount, "USD", BuildStoreUrl(dto.Slug ?? externalId));
    }

    /// <summary>GOG: https://www.gog.com/game/{slug}  (fallback на числовий Id)</summary>
    public string? BuildExternalUrl(string externalId, string? slug = null)
        => BuildStoreUrl(slug ?? externalId);

    private static string BuildStoreUrl(string slugOrId)
        => $"https://www.gog.com/game/{slugOrId}";

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.StartsWith("//") ? "https:" + url : url;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
