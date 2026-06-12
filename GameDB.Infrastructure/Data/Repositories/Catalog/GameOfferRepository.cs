using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public sealed class GameOfferRepository(AppDbContext context) : IGameOfferRepository
{
    /// <summary>
    /// Знаходить GameOffer безпосередньо по FK (GameExternalId.Id).
    /// Швидший шлях — використовується в PriceManagerService.
    /// </summary>
    public async Task<GameOffer?> GetByExternalIdRecordAsync(int externalIdRecordId, CancellationToken ct = default)
        => await context.GameOffers
            .FirstOrDefaultAsync(o => o.ExternalId == externalIdRecordId, ct);

    public async Task AddGameOfferAsync(GameOffer offer, CancellationToken ct = default)
    {
        await context.GameOffers.AddAsync(offer, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateGameOfferAsync(GameOffer offer, CancellationToken ct = default)
    {
        context.GameOffers.Update(offer);
        await context.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<int, GameOffer>> GetBulkByExternalIdRecordsAsync(
    IReadOnlyList<int> externalIdRecordIds,
    CancellationToken ct = default)
    => await context.GameOffers
        .Where(o => externalIdRecordIds.Contains(o.ExternalId))
        .ToDictionaryAsync(o => o.ExternalId, ct);

    public async Task BulkUpsertAsync(
        IReadOnlyList<GameOffer> toAdd,
        IReadOnlyList<GameOffer> toUpdate,
        CancellationToken ct = default)
    {
        if (toAdd.Count > 0)
            await context.GameOffers.AddRangeAsync(toAdd, ct);

        if (toUpdate.Count > 0)
            context.GameOffers.UpdateRange(toUpdate);


        await context.SaveChangesAsync(ct);
    }
}