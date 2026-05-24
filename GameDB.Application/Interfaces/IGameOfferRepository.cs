using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameOfferRepository
{
    Task<List<GameOffer>> GetByGameIdAsync(int gameId);
    Task<GameOffer?> GetByIdAsync(int offerId);
    Task AddAsync(GameOffer offer);
    Task UpdateAsync(GameOffer offer);
    Task DeleteAsync(int offerId);
}
