using GameDB.Application.Constants;
using GameDB.Application.DTOs;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using Microsoft.Extensions.Options;

namespace GameDB.Infrastructure.Providers;

public sealed class EGDataStoreProvider(
    IEGDataClient client,
    IOptions<EGDataImportOptions> options) : IStoreProvider
{  
    private readonly EGDataImportOptions _opts = options.Value;
    private const int PageLimit = 50;

    public int    ShopId                 => ShopIds.Epic;
    public string Slug                   => "epic";
    public int    DelayBetweenRequestsMs => _opts.DelayBetweenRequestsMs;

    public async Task<IReadOnlyCollection<StoreGameListItem>> GetGameListAsync(CancellationToken ct)
    {
        var result = new List<StoreGameListItem>();
        int page = 1;
        int consecutiveErrors = 0;
        const int maxRetries = 3; // Кількість спроб для однієї сторінки у разі помилки мережі

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            
            var dto = await client.GetItemsPageAsync(page, PageLimit, ct);
            
            // 1. ОБРОБКА ПОМИЛОК (Якщо повернувся null через HTTP-збій або таймаут)
            if (dto is null)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= maxRetries)
                {
                    // Замість break — логуємо і пропонуємо пропустити сторінку, щоб не вбивати весь імпорт
                    page++;
                    consecutiveErrors = 0;
                    continue;
                }
                
                // Робимо паузу трохи більшою, щоб сервер "оговтався"
                await Task.Delay(_opts.DelayBetweenRequestsMs * 2, ct); 
                continue;
            }

            // Якщо запит успішний — скидаємо лічильник помилок
            consecutiveErrors = 0;

            // 2. ПЕРЕВІРКА КІНЦЯ КАТАЛОГУ (Справжній вихід)
            // Якщо сервер повернув повністю порожній масив — це фінал
            if (!dto.HasDataFromServer) 
            {
                break;
            }

            // 3. ОБРОБКА ДАНИХ (Якщо сторінка не порожня ПІСЛЯ RemoveAll)
            foreach (var item in dto.Elements)
            {
                if (!string.IsNullOrWhiteSpace(item.Title))
                {
                    result.Add(new StoreGameListItem(item.Id.ToString(), item.Title, item.ProductSlug));
                }
            }
            
            // Переходимо далі, навіть якщо після RemoveAll на цій сторінці залишилось 0 ігор
            page++;
            await Task.Delay(_opts.DelayBetweenRequestsMs, ct);
        }

        return result;
    }

    public bool IsValidItem(StoreGameListItem item)
        => !string.IsNullOrWhiteSpace(item.ExternalId) && !string.IsNullOrWhiteSpace(item.Name);

    public async Task<StoreGameDetails?> GetGameDetailsAsync(string externalId, CancellationToken ct)
    {
        var dto = await client.GetItemDetailsAsync(externalId, ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Title)) return null;

        var genres = dto.Tags
            .Where(t => t.GroupName?.Equals("genre", StringComparison.OrdinalIgnoreCase) == true
                     && !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => t.Name!).ToArray();

        var tags = dto.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => t.Name!).Take(20).ToArray();

        return new StoreGameDetails
        {
            ExternalId     = externalId,
            Name           = dto.Title,
            Developer      = NullIfEmpty(dto.DeveloperDisplayName),
            Publisher      = NullIfEmpty(dto.PublisherDisplayName),
            Genres         = genres,
            Tags           = tags,
            Rating         = null,
            RatingCount    = null,
            HeaderImageUrl = FindImage(dto, "DieselStoreFrontTall", "OfferImageTall"),
            IconImageUrl   = FindImage(dto, "DieselGameBoxTall", "Thumbnail"),
        };
    }

    public async Task<StorePriceInfo?> GetPriceAsync(string externalId, CancellationToken ct)
    {
        var dto = await client.GetItemPriceAsync(externalId, ct);
        if (dto?.Price is null) return null;

        var price    = dto.Price;
        var discount = dto.Discount;
        var currency = dto.Currency;
        return new StorePriceInfo(price, discount, currency, BuildStoreUrl(externalId));
    }

    /// <summary>Epic: https://store.epicgames.com/en-US/p/{slug}  (fallback на externalId)</summary>
    public string? BuildExternalUrl(string externalId)
        => BuildStoreUrl(externalId);

    private static string BuildStoreUrl(string Id)
        => $"https://store.epicgames.com/en-US/p/{Id}";

    private static string? FindImage(EGDataItemDto dto, params string[] preferredTypes)
    {
        foreach (var type in preferredTypes)
        {
            var img = dto.KeyImages.FirstOrDefault(
                i => i.Type?.Equals(type, StringComparison.OrdinalIgnoreCase) == true);
            if (img?.Url is not null) return img.Url;
        }
        return dto.KeyImages.FirstOrDefault()?.Url;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
