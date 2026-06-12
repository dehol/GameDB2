using System.Diagnostics;
using GameDB.Application.DTOs.Store;
using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services.Import;

/// <summary>
/// [AutomaticRetry] читається Hangfire при виконанні (з типу реалізації).
/// Attempts = 0: не перезапускати автоматично — якщо sync впав після 6 годин,
/// адмін сам вирішить чи запускати знову.
/// </summary>
[AutomaticRetry(Attempts = 0, LogEvents = true, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class PriceSyncService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IStoreProvider> providers,
    IGameOfferRepository        offerRepository,
    PriceSyncOperationState state,
    ILogger<PriceSyncService> logger) : IPriceSyncService
{
    private readonly IReadOnlyList<IStoreProvider> _providers = providers.ToList();

    // Жорсткий ліміт на один провайдер. Якщо перевищено — job падає як Failed у Dashboard.
    // InvisibilityTimeout у PostgreSqlStorageOptions повинен бути БІЛЬШИМ за це значення.
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromHours(6);

    // Обмежує паралельні HTTP-запити в одному батчі — уникнення circuit breaker SteamSpy.
    private const int MaxParallelPriceFetches = 15;

    public PriceSyncOperationState State => state;

    // ── Ручний запуск (legacy, один job для всіх) ─────────────────────────────

    public async Task RunPriceSyncJobAsync(string? providerSlug, CancellationToken ct)
    {
        if (!state.TryStart())
        {
            logger.LogWarning("Спроба подвійного запуску синхронізації цін відхилена.");
            return;
        }

        try
        {
            var active = GetProviders(providerSlug);

            // Total = 0: кожен провайдер додасть свою кількість через AddToTotal
            state.ResetProgress(0, providerSlug ?? "Всі магазини", "Синхронізація цін");

            logger.LogInformation(
                "Синхронізація цін (legacy): {Count} провайдерів — паралельно",
                active.Count);

            await Task.WhenAll(active.Select(p => RunProviderInternalAsync(p, ct)));

            var msg = $"Завершено. Оновлено: {state.Processed}, Помилок: {state.Failed}";
            state.MarkFinished(msg);
            logger.LogInformation("✅ {Msg}", msg);
        }
        catch (OperationCanceledException)
        {
            state.MarkFinished($"Зупинено. Оновлено: {state.Processed}, Помилок: {state.Failed}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критична помилка під час синхронізації цін");
            state.LastError = ex.Message;
            state.MarkFinished($"Аварійне завершення. Оновлено: {state.Processed}");
        }
    }

    // ── Запуск одного провайдера (основний шлях через Hangfire) ──────────────
    // CancellationToken.None у Enqueue — Hangfire замінює на ShutdownToken при виконанні.

    public async Task SyncProviderAsync(string providerSlug, CancellationToken ct)
    {
        var provider = _providers.FirstOrDefault(p =>
            p.Slug.Equals(providerSlug, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            logger.LogError(
                "Provider не знайдено: '{Slug}'. Доступні: {All}",
                providerSlug,
                string.Join(", ", _providers.Select(p => p.Slug)));
            if (state.NotifyProviderFinished())
                state.MarkFinished($"Помилка: провайдер '{providerSlug}' не знайдено.");
            return;
        }

        // Лінкуємо 3 джерела скасування:
        //   ct              — Hangfire ShutdownToken (зупинка сервера)
        //   timeoutCts      — жорсткий ліміт 6 год на провайдер
        //   state.StopToken — кнопка "Stop" в адмін-панелі
        using var timeoutCts = new CancellationTokenSource(ProviderTimeout);
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(
            ct, timeoutCts.Token, state.StopToken);

        state.NotifyProviderStarted(providerSlug);

        try
        {
            await RunProviderInternalAsync(provider, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogError(
                "[{Slug}] ❌ Перевищено ліміт {H} год. Збільш ProviderTimeout або BatchSize.",
                provider.Slug, ProviderTimeout.TotalHours);
            throw; // Hangfire → Failed
        }
        finally
        {
            // Останній провайдер що завершився оновлює загальний стан → UI показує підсумок
            if (state.NotifyProviderFinished())
            {
                var summary = $"Синхронізацію завершено. Оновлено: {state.Processed}, Помилок: {state.Failed}";
                state.MarkFinished(summary);
                logger.LogInformation("✅ Всі провайдери завершено. {Summary}", summary);
            }
        }
    }

    // ── Спільна логіка для обох точок входу ──────────────────────────────────

    private async Task RunProviderInternalAsync(IStoreProvider provider, CancellationToken ct)
    {
        const int batchSize = 100;
        int skip            = 0;
        int providerUpdated = 0;
        int providerSkipped = 0;
        int providerFailed  = 0;
        var sw              = Stopwatch.StartNew();

        logger.LogInformation("[{Slug}] ▶ Синхронізація цін починається", provider.Slug);

        // Зупинка через кнопку "Stop" — state.StopToken скасовує ct через LinkedCts у SyncProviderAsync.
        // IsRunning не перевіряється: SyncProviderAsync не викликає TryStart(),
        // тому IsRunning = false для Hangfire-шляху завжди.
        while (!ct.IsCancellationRequested)
        {
            List<Game> batch;
            using (var scope = scopeFactory.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

                batch = await repo.GetGamesBatchFromShopAsync(skip, batchSize, provider.ShopId, ct);

                // Перший батч: реєструємо кількість ігор цього магазину для прогрес-бару.
                // Використовуємо shop-specific count, щоб паралельні провайдери
                // не зараховували загальний Total тричі.
                if (skip == 0)
                {
                    var shopCount = await repo.GetGameCountByShopAsync(provider.ShopId, ct);
                    if (shopCount > 0)
                        state.AddToTotal(shopCount);
                }
            }

            if (batch.Count == 0) break;

            var (updated, skipped, failed) = await ProcessBatchAsync(provider, batch, ct);

            providerUpdated += updated;
            providerSkipped += skipped;
            providerFailed  += failed;
            skip            += batch.Count;

            logger.LogInformation(
                "[{Slug}] Батч {From}–{To}: " +
                "✅ оновлено {U} | ⚪ без змін/null {S} | ❌ помилок {F} " +
                "— Всього: {TU}/{TF} | ⏱ {Elapsed:hh\\:mm\\:ss}",
                provider.Slug,
                skip - batch.Count, skip,
                updated, skipped, failed,
                providerUpdated, providerFailed,
                sw.Elapsed);
        }

        sw.Stop();

        var status = ct.IsCancellationRequested ? "⏹ Скасовано" : "✅ Завершено";
        logger.LogInformation(
            "[{Slug}] {Status}: оновлено {U}, без змін {S}, помилок {F} | Час: {Elapsed:hh\\:mm\\:ss}",
            provider.Slug, status, providerUpdated, providerSkipped, providerFailed, sw.Elapsed);
    }

    // ── Обробка одного батчу ──────────────────────────────────────────────────

    private async Task<(int updated, int skipped, int failed)> ProcessBatchAsync(
    IStoreProvider provider,
    IReadOnlyCollection<Game> batch,
    CancellationToken ct)
    {
        var pairs = batch
            .SelectMany(g => g.GameExternalIds
                .Where(e => e.ShopId == provider.ShopId)
                .Select(e => (Game: g, ExternalIdRecord: e)))
            .ToList();

        // ── Фаза 1: паралельний HTTP з обмеженням ────────────────────────────────
        using var fetchGate = new SemaphoreSlim(MaxParallelPriceFetches);

        var fetchTasks = pairs.Select(async pair =>
        {
            await fetchGate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var price = await provider.GetPriceAsync(pair.ExternalIdRecord.ExternalId, ct);
                    return (RecordId: pair.ExternalIdRecord.Id, Price: price, Error: (Exception?)null);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    return (RecordId: pair.ExternalIdRecord.Id, Price: (StorePriceInfo?)null, Error: ex);
                }
            }
            finally
            {
                fetchGate.Release();
            }
        });

        var results = await Task.WhenAll(fetchTasks);

        // ── Фаза 2: BULK запис ────────────────────────────────────────────────────

        // Розбиваємо результати на три категорії
        var toProcess = results.Where(r => r.Error is null && r.Price is not null).ToList();
        var failed    = results.Count(r => r.Error is not null);
        var skipped   = results.Count(r => r.Error is null && r.Price is null);

        if (toProcess.Count == 0)
        {
            state.AddFailed(failed);
            return (0, skipped, failed);
        }

        // 1 SELECT для всіх успішних результатів
        var recordIds       = toProcess.Select(r => r.RecordId).ToList();
        var existingOffers  = await offerRepository.GetBulkByExternalIdRecordsAsync(recordIds, ct);

        var toAdd    = new List<GameOffer>();
        var toUpdate = new List<GameOffer>();

        foreach (var (recordId, price, _) in toProcess)
        {
            if (existingOffers.TryGetValue(recordId, out var existing))
            {
                // Оновлюємо поля прямо на об'єкті — EF Core відстежує зміни
                existing.CurrentPrice    = price!.Price;
                existing.CurrentDiscount = price.Discount;
                existing.Currency        = price.Currency;
                existing.LastSyncedAt    = DateTime.UtcNow;
                toUpdate.Add(existing);
            }
            else
            {
                toAdd.Add(new GameOffer
                {
                    ExternalId      = recordId,
                    CurrentPrice    = price!.Price,
                    CurrentDiscount = price.Discount,
                    Currency        = price.Currency,
                    LastSyncedAt    = DateTime.UtcNow,
                });
            }
        }

        // 1 SaveChangesAsync для всіх INSERT + UPDATE разом
        await offerRepository.BulkUpsertAsync(toAdd, toUpdate, ct);

        var updated = toProcess.Count;
        state.AddToProcessed(updated);
        state.AddFailed(failed);

        return (updated, skipped, failed);
    }

    // ── Хелпери ───────────────────────────────────────────────────────────────

    private IReadOnlyList<IStoreProvider> GetProviders(string? slug)
        => string.IsNullOrEmpty(slug)
            ? _providers
            : _providers
                .Where(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
                .ToList();
}
