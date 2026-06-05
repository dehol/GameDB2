using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.Services;

public sealed class AlertCheckerHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AlertCheckerHostedService> logger) : BackgroundService
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
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var db     = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();

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

            db.Notifications.Add(new Notification
            {
                UserId      = alert.UserId,
                Type        = "price_alert",
                Description =
                    $"Ціна «{alert.Game.Name}» впала до {best:0.##} (ціль: ≤ {alert.TargetPrice:0.##})",
                IsRead      = false,
                CreatedAt   = DateTime.UtcNow,
            });
            triggered++;
        }

        if (triggered > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Спрацювало {Count} price-алертів", triggered);
        }
    }

    private static decimal? GetBestPrice(Alert alert)
    {
        // 1. Беремо зовнішні ID гри
        var externalIds = alert.Game.GameExternalIds;

        // 2. Якщо в алерті задано конкретний магазин, фільтруємо СУТНОСТІ ЗОВНІШНІХ ID за цим магазином
        if (alert.ShopId.HasValue)
        {
            externalIds = externalIds.Where(e => e.ShopId == alert.ShopId.Value).ToList(); // або без ToList, якщо це колекція в пам'яті
        }

        // 3. Тепер збираємо оффери тільки з потрібних (відфільтрованих) магазинів
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
