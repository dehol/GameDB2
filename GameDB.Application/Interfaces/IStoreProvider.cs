using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.DTOs.Store;

namespace GameDB.Application.Interfaces;

public interface IStoreProvider
{
    int    ShopId                  { get; }
    string Slug                    { get; }
    int    DelayBetweenRequestsMs  { get; }

    Task<IReadOnlyCollection<StoreGameListItem>> GetGameListAsync(CancellationToken ct);
    bool IsValidItem(StoreGameListItem item);
    Task<StoreGameDetails?> GetGameDetailsAsync(string externalId, CancellationToken ct);
    Task<StorePriceInfo?> GetPriceAsync(string externalId, CancellationToken ct);

    /// <summary>
    /// Будує ExternalUrl для сторінки гри в магазині.
    /// Steam: https://store.steampowered.com/app/{externalId}/
    /// GOG:   https://www.gog.com/game/{slug}  (fallback на externalId)
    /// Epic:  https://store.epicgames.com/en-US/p/{slug}  (fallback на externalId)
    /// </summary>
    string? BuildExternalUrl(string externalId, string? slug = null);
}
