using System.Threading;
using System.Threading.Tasks;
using GameDB.Application.DTOs;

namespace GameDB.Application.Interfaces;

public interface IEGDataClient
{
    /// <summary>Отримує одну сторінку ігор Epic Games (egdata.app).</summary>
    Task<EGDataListResponseDto?> GetItemsPageAsync(int page, int limit, CancellationToken ct = default);

    /// <summary>Деталі конкретного офера за ID.</summary>
    Task<EGDataItemDto?> GetItemDetailsAsync(string itemId, CancellationToken ct = default);
}