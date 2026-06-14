using GameDB.Application.Constants;
using GameDB.Application.DTOs;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Providers;

public sealed class EGDataStoreProvider(
    IEGDataClient client,
    ILogger<GogStoreProvider> logger) : IStoreProvider
{
    private const int PageLimit = 50;

    public int    ShopId                 => ShopIds.Epic;
    public string Slug                   => "epic";

    public async Task<IReadOnlyCollection<StoreGameListItem>> GetGameListAsync(CancellationToken ct)
    {
        var result = new List<StoreGameListItem>();
        int page = 1;
        var dtoFirst = await client.GetItemsPageAsync(page, PageLimit, ct);
        int iterations = dtoFirst.Total / dtoFirst.Limit;

        for (int i = 0; i < iterations; i++)
        {
            Console.WriteLine(i);
            ct.ThrowIfCancellationRequested();

            var dto = await client.GetItemsPageAsync(page, PageLimit, ct);

            if(dto == null)
            {
                continue;
            }
            

            foreach (var item in dto.Elements)
            {
                if (!string.IsNullOrWhiteSpace(item.Title))
                {
                    result.Add(new StoreGameListItem(item.Id.ToString(), item.Title, item.ProductSlug));
                    logger.LogInformation(item.Title);
                }
                
            }

            page++;
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
            Description    = NullIfEmpty(dto.Description),
        };
    }

    public Task<StorePriceInfo?> GetPriceAsync(string externalId, CancellationToken ct)
        => client.GetItemPriceAsync(externalId, ct);

    public string BuildOfferUrl(string slugOrId)
        => $"https://store.epicgames.com/en-US/p/{slugOrId}";

    private static string? FindImage(EGDataItemDto dto, params string[] preferredTypes)
    {
        foreach (var type in preferredTypes)
        {   
            if(dto.KeyImages == null) break;
            var img = dto.KeyImages.FirstOrDefault(
                i => i.Type?.Equals(type, StringComparison.OrdinalIgnoreCase) == true);
            if (img?.Url is not null) return img.Url;
        }
        return dto.KeyImages.FirstOrDefault().Url;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}