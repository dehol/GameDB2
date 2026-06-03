using GameDB.Application.Constants;
using GameDB.Application.DTOs.Store;
using GameDB.Application.DTOs;
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
        var seenIds = new HashSet<string>();

        // Перша сторінка — дізнаємося реальний totalPages з API
        var firstDto = await client.GetGamesPageAsync(1, ct);
        int totalPages = firstDto?.TotalPages ?? 0;
        if (totalPages == 0) return result;
        ProcessPage(firstDto, seenIds, result);

        for (int page = 2; page <= totalPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var dto = await client.GetGamesPageAsync(page, ct);
            if (dto?.Products != null)
                ProcessPage(dto, seenIds, result);
            await Task.Delay(150, ct);
        }

        return result;
    }

    private static void ProcessPage(
        GogFilteredResponseDto? dto,
        HashSet<string> seenIds,
        List<StoreGameListItem> result)
    {
        if (dto?.Products == null) return;
        foreach (var p in dto.Products)
        {
            var id = p.Id.ToString();
            if (string.IsNullOrWhiteSpace(id) || !seenIds.Add(id)) continue;
            if (!string.IsNullOrWhiteSpace(p.Title))
                result.Add(new StoreGameListItem(id, p.Title, p.Slug));
            
        }
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
        var dto = await client.GetItemPriceAsync(externalId, ct);
        if (dto?.Price is null) return null;

        var price = dto.Price;
        var discount = dto.Discount;
        return new StorePriceInfo(price, discount, "USD", BuildStoreUrl(externalId));
    }

    /// <summary>GOG: https://www.gog.com/game/{slug}  (fallback на числовий Id)</summary>
    public string? BuildExternalUrl(string externalId)
        => BuildStoreUrl(externalId);

    private static string BuildStoreUrl(string Id)
        => $"https://www.gog.com/game/{Id}";

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.StartsWith("//") ? "https:" + url : url;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool ShouldStop(Queue<int> recentPages, double minNewIdRatio)
{
    if (recentPages.Count < 10)
        return false;

    int totalPages = recentPages.Count;
    int totalNewIds = recentPages.Sum();

    int maxPossible = totalPages * 48;

    double ratio = maxPossible == 0
        ? 0
        : (double)totalNewIds / maxPossible;

    // якщо менше 5% нових ID → кінець каталогу
    return ratio < minNewIdRatio;
}
}
