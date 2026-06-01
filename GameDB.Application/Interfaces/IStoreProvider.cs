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
}
