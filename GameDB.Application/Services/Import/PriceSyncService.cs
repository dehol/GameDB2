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
    IPriceManagerService priceManager,
    PriceSyncOperationState state,
    ILogger<PriceSyncService> logger) : IPriceSyncService
{
    private readonly IReadOnlyList<IStoreProvider> _providers = providers.ToList();

    // Жорсткий ліміт на один провайдер. Якщо перевищено — job падає як Failed у Dashboard.
    // InvisibilityTimeout у PostgreSqlStorageOptions повинен бути БІЛЬШИМ за це значення.
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromHours(6);

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
            var total  = await CountGamesAsync(ct);

            state.ResetProgress(total, providerSlug ?? "Всі магазини", "Синхронізація цін");

            logger.LogInformation(
                "Синхронізація цін (legacy): {Count} провайдерів, ~{Total} ігор — паралельно",
                active.Count, total);

            // Паралельний запуск і в legacy-методі
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

    // ── Запуск одного провайдера (новий шлях через Hangfire) ─────────────────
    // CancellationToken.None у Enqueue — Hangfire замінює на ShutdownToken при виконанні.
    // Всередині додаємо жорсткий таймаут через LinkedCts.
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
            // Все одно зменшуємо лічильник — провайдер "завершив" з помилкою
            if (state.NotifyProviderFinished())
                state.MarkFinished($"Помилка: провайдер '{providerSlug}' не знайдено.");
            return;
        }

        // Лінкуємо 3 джерела скасування:
        //   ct              — Hangfire ShutdownToken (зупинка сервера)
        //   timeoutCts      — наш жорсткий ліміт 6 год
        //   state.StopToken — кнопка "Stop" в адмін-панелі
        using var timeoutCts = new CancellationTokenSource(ProviderTimeout);
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(
            ct, timeoutCts.Token, state.StopToken);

        // Сигналізуємо UI що цей провайдер починає (для рядка CurrentProvider)
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
            // Останній провайдер що завершився (незалежно від успіху/помилки)
            // оновлює загальний стан → UI показує підсумок
            if (state.NotifyProviderFinished())
            {
                var summary = $"Синхронізацію завершено. Оновлено: {state.Processed}, Помилок: {state.Failed}";
                state.MarkFinished(summary);
                logger.LogInformation("✅ Всі провайдери завершено. {Summary}", summary);
            }
        }
        // OperationCanceledException через Hangfire ShutdownToken —
        // Hangfire сам перенесе job в Scheduled для наступного запуску.
    }

    // ── Спільна логіка для обох точок входу ──────────────────────────────────
    private async Task RunProviderInternalAsync(IStoreProvider provider, CancellationToken ct)
    {
        const int batchSize = 100;
        int skip             = 0;
        int providerUpdated  = 0;
        int providerSkipped  = 0;
        int providerFailed   = 0;
        var sw               = Stopwatch.StartNew();

        logger.LogInformation("[{Slug}] ▶ Синхронізація цін починається", provider.Slug);

        // ВАЖЛИВО: тут тільки ct, не state.IsRunning.
        // SyncProviderAsync не викликає TryStart() → IsRunning завжди false для Hangfire-шляху.
        // Зупинка через кнопку "Stop" — state.StopToken скасовує ct через LinkedCts у SyncProviderAsync.
        while (!ct.IsCancellationRequested)
        {
            List<Game> batch;
            int totalInRepo;
            using (var scope = scopeFactory.CreateScope())
            {
                var repo     = scope.ServiceProvider.GetRequiredService<IGameRepository>();
                batch        = await repo.GetGamesBatchFromShopAsync(skip, batchSize, provider.ShopId, ct);
                totalInRepo  = skip == 0 ? await repo.GetTotalGamesCountAsync(ct) : 0;
            }

            // Перший батч — додаємо кількість ігор провайдера до загального Total для прогрес-бару
            if (skip == 0 && totalInRepo > 0)
                state.AddToTotal(totalInRepo);

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

        // ── Фаза 1: паралельний fetch цін ─────────────────────────────────────
        // ConcurrencyLimiter в Polly pipeline контролює скільки HTTP-запитів
        // одночасно йде через клієнт — DelayBetweenRequestsMs між запитами більше
        // не потрібен, throttling тепер через Polly.
        //
        // Не-скасувальні виключення захоплюються в tuple.Error — Task.WhenAll
        // завершується повністю, а не зупиняється на першій помилці.
        var fetchTasks = pairs.Select(async pair =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var price = await provider.GetPriceAsync(pair.ExternalIdRecord.ExternalId, ct);
                return (RecordId: pair.ExternalIdRecord.Id, Price: price, Error: (Exception?)null);
            }
            catch (OperationCanceledException)
            {
                throw; // пробрасуємо — це сигнал зупинки, не помилка провайдера
            }
            catch (Exception ex)
            {
                return (RecordId: pair.ExternalIdRecord.Id, Price: (StorePriceInfo?)null, Error: ex);
            }
        });

        var results = await Task.WhenAll(fetchTasks);

        // ── Фаза 2: послідовне збереження ─────────────────────────────────────
        // DbContext не thread-safe — зберігаємо послідовно.
        // DB-операції швидкі (прості UPDATE), вузьке місце було в HTTP — його вже вирішено.
        int updated = 0, skipped = 0, failed = 0;

        foreach (var (recordId, price, error) in results)
        {
            ct.ThrowIfCancellationRequested();

            if (error is not null)
            {
                state.IncrementFailed();
                failed++;
                logger.LogWarning(
                    "[{Slug}] Не вдалося отримати ціну для {Id}: {Message}",
                    provider.Slug, recordId, error.Message);
                continue;
            }

            if (price is null)
            {
                // Гра є в БД, але ціна недоступна (регіон, знята з продажу тощо)
                skipped++;
                continue;
            }

            await priceManager.ProcessPriceUpdateAsync(
                externalIdRecordId: recordId,
                newPrice:           price.Price,
                newDiscount:        price.Discount,
                currency:           price.Currency,
                ct:                 ct);

            state.IncrementProcessed();
            updated++;
        }

        return (updated, skipped, failed);
    }

    // ── Хелпери ───────────────────────────────────────────────────────────────
    private IReadOnlyList<IStoreProvider> GetProviders(string? slug)
        => string.IsNullOrEmpty(slug)
            ? _providers
            : _providers
                .Where(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
                .ToList();

    private async Task<int> CountGamesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        return await repo.GetTotalGamesCountAsync(ct);
    }
}