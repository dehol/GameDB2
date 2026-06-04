using GameDB.Application.Constants;
using GameDB.Application.DTOs.Store;
using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Providers;

public sealed class GogStoreProvider(
    IGogClient client,
    IOptions<GogImportOptions> options,
    ILogger<GogStoreProvider> logger) : IStoreProvider
{
    private readonly GogImportOptions _opts = options.Value;

    public int    ShopId                 => ShopIds.Gog;
    public string Slug                   => "gog";
    public int    DelayBetweenRequestsMs => _opts.DelayBetweenRequestsMs;
    
    public async Task<IReadOnlyCollection<StoreGameListItem>> GetGameListAsync(CancellationToken ct)
    {
        var result  = new List<StoreGameListItem>();
        var seenIds = new HashSet<string>();
        
        // Початковий курсор для GOG API
        string cursor = "0"; 

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            logger.LogInformation("Fetching GOG catalog with cursor: {Cursor}", cursor);

            var dto = await client.GetCatalogPageAsync(cursor, ct);
            
            // Якщо прийшла порожня відповідь або список продуктів порожній — ми дійшли кінця
            if (dto?.Products is null || dto.Products.Count == 0)
            {
                logger.LogInformation("Reached the end of GOG catalog.");
                break;
            }

            ProcessCatalogPage(dto, seenIds, result);

            // Знаходимо останній валідний ID на поточній сторінці, щоб використати як наступний курсор
            var lastProduct = dto.Products.LastOrDefault(p => !string.IsNullOrWhiteSpace(p.Id));
            if (lastProduct is null)
            {
                break; // Безпечний вихід, якщо раптом у відповіді біда з айдішниками
            }

            cursor = lastProduct.Id;

            // Оптимізація: якщо сервер повернув менше продуктів ніж ліміт (48), це остання сторінка
            if (dto.Products.Count < 48)
            {
                break;
            }

            await Task.Delay(_opts.DelayBetweenRequestsMs, ct);
        }

        return result;
    }

    private void ProcessCatalogPage(
        GogCatalogResponseDto dto,
        HashSet<string> seenIds,
        List<StoreGameListItem> result)
    {
        foreach (var p in dto.Products)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || !seenIds.Add(p.Id)) continue;
            if (!string.IsNullOrWhiteSpace(p.Title))
            {
                logger.LogInformation(p.Title);
                result.Add(new StoreGameListItem(p.Id, p.Title, p.Slug));
            }
        }
    }

    public bool IsValidItem(StoreGameListItem item)
        => !string.IsNullOrWhiteSpace(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Name);

    public async Task<StoreGameDetails?> GetGameDetailsAsync(string externalId, CancellationToken ct)
    {
        var dto = await client.GetProductDetailsAsync(externalId, ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Title)) return null;

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
}