namespace GameDB.Application.Interfaces;

public interface IPriceManagerService
{
    /// <summary>
    /// Оновлює або створює GameOffer для вказаного запису GameExternalId.
    /// PriceHistory управляється автоматично тригером fn_sync_price_history.
    /// </summary>
    /// <param name="externalIdRecordId">
    /// GameExternalId.Id — цілочисельний PK запису, що зв'язує гру з магазином.
    /// Отримується з <c>game.GameExternalIds.First(e => e.ShopId == shopId).Id</c>.
    /// </param>
    Task ProcessPriceUpdateAsync(
        int               externalIdRecordId,
        decimal           newPrice,
        short             newDiscount,
        string            currency,
        CancellationToken ct = default);
}
