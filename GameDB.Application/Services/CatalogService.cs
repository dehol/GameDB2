using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;

namespace GameDB.Application.Services;

public class CatalogService
{
    private readonly IGameOfferRepository _gameOffers;
    private readonly IGameRepository _games;

    public CatalogService(IGameOfferRepository gameOffers, IGameRepository games)
    {
        _gameOffers = gameOffers;
        _games = games;
    }

    public async Task<(List<GameSummaryDto> items, int totalCount)> GetCatalogAsync()
    {
        throw new NotImplementedException();
        /*var games = await _games.GetAllAsync();
        var items = new List<GameSummaryDto>();

        foreach (var game in games)
        {
            var offers = await _gameOffers.GetByGameIdAsync(game.GameId);
            var offer = offers.FirstOrDefault();

            items.Add(new GameSummaryDto(
                GameId: game.GameId,
                Name: game.Name,
                Slug: game.Slug,
                HeaderImage: game.HeaderImage,
                ReleaseDate: game.ReleaseDate,
                CurrentPrice: offer?.CurrentPrice ?? 0m,
                CurrentDiscount: offer?.CurrentDiscount ?? 0
            ));
        }

        return (items, items.Count);*/
    }
}
