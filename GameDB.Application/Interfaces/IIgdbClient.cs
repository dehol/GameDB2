using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IIgdbClient
{
    Task<IReadOnlyList<IgdbGameDto>> SearchGamesAsync(string gameName, CancellationToken ct = default);
}