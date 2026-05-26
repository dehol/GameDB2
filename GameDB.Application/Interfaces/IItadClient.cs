// Файл: GameDB.Application/Interfaces/IItadClient.cs
using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IItadClient
{
    Task<Dictionary<string, string>> GetUuidsBySteamIdsAsync(List<int> steamIds, CancellationToken ct = default);
    Task<List<ItadPriceResponse>> GetPricesAsync(List<string> itadUuids, CancellationToken ct = default);
}