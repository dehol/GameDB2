using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Workers;

/// <summary>
/// Фоновий сервіс перевірки price-алертів (кожні 15 хвилин).
///
/// FIX: Раніше резолвив AppDbContext напряму через DI scope і викликав
/// db.Notifications.Add(...) / db.SaveChangesAsync() — пряма залежність
/// Infrastructure-сервісу на конкретний DbContext в обхід репозиторій-шару.
///
/// Тепер: INotificationRepository ізолює запис нотифікацій за інтерфейсом.
/// IAlertRepository вже використовувався раніше — паттерн тепер послідовний.
/// </summary>
public sealed class AlertCheckerHostedService(
    IServiceScopeFactory                scopeFactory,
    ILogger<AlertCheckerHostedService>  logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAlertsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Помилка перевірки price-алертів");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CheckAlertsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var alerts        = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var active = await alerts.GetActiveAlertsAsync();
        if (active.Count == 0) return;

        var triggered = 0;

        foreach (var alert in active)
        {
            if (!alert.TargetPrice.HasValue) continue;

            var best = GetBestPrice(alert);
            if (!best.HasValue || best <= 0) continue;

            if (alert.AutoUpdate && alert.ReferenceLowest.HasValue && best < alert.ReferenceLowest)
            {
                alert.TargetPrice = alert.AutoUpdateMode == "MatchLowest"
                    ? best
                    : BeatPrice(best.Value);
                alert.ReferenceLowest = best;
                await alerts.UpdateAsync(alert);
            }
            else if (alert.AutoUpdate && !alert.ReferenceLowest.HasValue)
            {
                alert.ReferenceLowest = best;
                await alerts.UpdateAsync(alert);
            }

            if (best > alert.TargetPrice.Value)
                continue;

            alert.TriggeredAt    = DateTime.UtcNow;
            alert.LastNotifiedAt = DateTime.UtcNow;
            await alerts.UpdateAsync(alert);

            notifications.Add(new Notification
            {
                UserId      = alert.UserId,
                Type        = "price_alert",
                Description = $"Ціна «{alert.Game.Name}» впала до {best:0.##} (ціль: ≤ {alert.TargetPrice:0.##})",
                IsRead      = false,
                CreatedAt   = DateTime.UtcNow,
            });
            triggered++;
        }

        if (triggered > 0)
        {
            await notifications.SaveChangesAsync(ct);
            logger.LogInformation("Спрацювало {Count} price-алертів", triggered);
        }
    }

    private static decimal? GetBestPrice(Alert alert)
    {
        var externalIds = alert.Game.GameExternalIds;

        if (alert.ShopId.HasValue)
            externalIds = externalIds.Where(e => e.ShopId == alert.ShopId.Value).ToList();

        var offers = externalIds.SelectMany(e => e.GameOffers);

        decimal? best = null;
        foreach (var o in offers)
        {
            var price = o.FinalPrice ?? o.CurrentPrice;
            if (price > 0 && (best is null || price < best))
                best = price;
        }

        return best;
    }

    private static decimal BeatPrice(decimal price)
        => price >= 1m ? Math.Max(0.01m, price - 0.01m) : price * 0.99m;
}
