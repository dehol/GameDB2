using GameDB.Application.DTOs;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameDB.Infrastructure.BackgroundServices;

/// <summary>
/// Фоновий сервіс, що кожні 30 хвилин перевіряє активні цінові алерти.
/// Якщо поточна ціна ≤ цільовій — встановлює TriggeredAt і створює Notification.
/// </summary>
public sealed class AlertCheckerHostedService(
    IServiceScopeFactory factory,
    ILogger<AlertCheckerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Невелика пауза при старті, щоб застосунок встиг підняти всі залежності
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAlertsAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Необроблена помилка під час перевірки алертів");
            }

            await Task.Delay(CheckInterval, ct);
        }
    }

    private async Task CheckAlertsAsync(CancellationToken ct)
    {
        await using var scope = factory.CreateAsyncScope();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IGameAlertRepository>();
        var notifRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var alerts = await alertRepo.GetActiveAlertsAsync(ct);
        if (alerts.Count == 0) return;

        logger.LogInformation("AlertChecker: перевіряємо {Count} активних алертів", alerts.Count);

        bool anyChanged = false;

        foreach (var alert in alerts)
        {
            if (!alert.TargetPrice.HasValue) continue;

            // Якщо алерт прив'язаний до конкретного магазину — враховуємо лише його оффери
            var offers = alert.Game.GameExternalIds
                .Where(e => !alert.ShopId.HasValue || e.ShopId == alert.ShopId.Value)
                .SelectMany(e => e.GameOffers)
                .ToList();

            if (offers.Count == 0) continue;

            var currentLowest = offers
                .Select(o => o.FinalPrice ?? o.CurrentPrice)
                .Where(p => p > 0)
                .DefaultIfEmpty(decimal.MaxValue)
                .Min();

            if (currentLowest == decimal.MaxValue) continue;

            // AutoUpdate: якщо ціна впала нижче записаного мінімуму — оновлюємо опорне значення.
            // MatchLowest додатково рухає цільову ціну вниз разом з мінімумом (нотифікація при кожному новому low).
            // BeatLowest зберігає оригінальну цільову ціну (нотифікація лише коли ціна б'є цю планку).
            if (alert.AutoUpdate
                && alert.ReferenceLowest.HasValue
                && currentLowest < alert.ReferenceLowest.Value)
            {
                if (alert.AutoUpdateMode == nameof(AlertAutoUpdateMode.MatchLowest))
                    alert.TargetPrice = currentLowest;

                alert.ReferenceLowest = currentLowest;
                anyChanged = true;
            }

            if (currentLowest > alert.TargetPrice.Value) continue;

            // ── Тригер ────────────────────────────────────────────────────────
            alert.TriggeredAt = DateTime.UtcNow;
            anyChanged = true;

            var currency = offers.FirstOrDefault()?.Currency ?? "USD";

            notifRepo.Add(new Notification
            {
                UserId      = alert.UserId,
                Type        = "PriceAlert",
                IsRead      = false,
                CreatedAt   = DateTime.UtcNow,
                Description = $"Ціна на «{alert.Game.Name}» знизилась до " +
                              $"{currentLowest:F2} {currency} " +
                              $"(ваша ціль: {alert.TargetPrice:F2} {currency}).",
            });

            logger.LogInformation(
                "AlertChecker: алерт #{AlertId} (гра={GameName}, userId={UserId}) спрацював — " +
                "{CurrentLowest} ≤ {Target} {Currency}",
                alert.AlertId, alert.Game.Name, alert.UserId,
                currentLowest, alert.TargetPrice.Value, currency);
        }

        if (anyChanged)
        {
            // Зберігаємо одним SaveChanges: оновлені Alert (TriggeredAt, AutoUpdate поля)
            // + нові Notification — все в одному DbContext scope
            await notifRepo.SaveChangesAsync(ct);
        }

        logger.LogInformation("AlertChecker: перевірку завершено ({Count} алертів оброблено)", alerts.Count);
    }
}
