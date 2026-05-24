using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public class GameOfferRepository : IGameOfferRepository
{
    private readonly AppDbContext _db;

    public GameOfferRepository(AppDbContext db) => _db = db;

    public async Task<List<GameOffer>> GetByGameIdAsync(int gameId)
        => await _db.GameOffers
            .Where(o => o.GameId == gameId)
            .ToListAsync();

    public async Task<GameOffer?> GetByIdAsync(int offerId)
        => await _db.GameOffers.FindAsync(offerId);

    public async Task AddAsync(GameOffer offer)
    {
        _db.GameOffers.Add(offer);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(GameOffer offer)
    {
        _db.GameOffers.Update(offer);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int offerId)
    {
        await _db.GameOffers
            .Where(o => o.GameOfferId == offerId)
            .ExecuteDeleteAsync();
    }
}
