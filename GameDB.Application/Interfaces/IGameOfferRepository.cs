using GameDB.Application.DTOs;
using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IGameOfferRepository
{
    Task<GameOffer?> GetGameOfferAsync(int gameId, int shopId, CancellationToken ct = default);
    Task AddGameOfferAsync(GameOffer offer, CancellationToken ct = default);
    Task UpdateGameOfferAsync(GameOffer offer, CancellationToken ct = default);
    Task<List<GameOffer>> GetByGameIdAsync(int gameId, CancellationToken ct = default);

    /// <summary>
    /// Дані для графіка ціни у SteamDB-стилі.
    /// Кожна точка = сегмент [PeriodStart, PeriodEnd) з фіксованою ціною.
    /// PeriodEnd для останнього запису = LastSyncedAt (ціна актуальна до цього моменту).
    /// </summary>
}

// Що ВИДАЛЕНО порівняно з попередньою версією:
//   Task<PriceHistory?> GetLatestPriceHistoryAsync(int gameOfferId)
//   Task AddPriceHistoryAsync(PriceHistory history)
//
// Причина: тригер fn_sync_price_history на GameOffer AFTER UPDATE
// автоматично вирішує INSERT новий рядок або UPDATE LastSyncedAt.
// C# більше не керує PriceHistory напряму.