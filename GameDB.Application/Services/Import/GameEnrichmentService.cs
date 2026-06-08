using GameDB.Application.Interfaces;
using GameDB.Domain.Enums;
using GameDB.Domain.Entities;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Збагачення ігор деталями від провайдерів магазинів.
///
/// Два шляхи запуску:
///   1. EnrichProviderAsync — основний шлях: AdminService ставить 3 окремих
///      Hangfire-job'и (по одному на провайдер) → виконуються паралельно.
///      Кожен job захищений своїм InvisibilityTimeout (8h) незалежно.
///
///   2. RunEnrichmentJobAsync — legacy: один job з Task.WhenAll всередині.
///      Залишено для ручного запуску через Dashboard.
///
/// [AutomaticRetry(Attempts = 0)] — не перезапускати автоматично:
/// якщо збагачення впало після годин роботи — адмін сам вирішить.
/// </summary>
[AutomaticRetry(Attempts = 0, LogEvents = true, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class GameEnrichmentService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IStoreProvider> providers,
    StoreGameMapper mapper,
    EnrichmentOperationState state,
    ILogger<GameEnrichmentService> logger) : IGameEnrichmentService
{
    private readonly IReadOnlyList<IStoreProvider> _providers = providers.ToList();

    // Жорсткий ліміт на один провайдер.
    // InvisibilityTimeout (8h) > ProviderTimeout (7h) — Hangfire ніколи не
    // вирішить що job завис поки він сам себе не завершив або не впав.
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromHours(7);

    public EnrichmentOperationState State => state;

    // ── Основний шлях: один провайдер, окремий Hangfire-job ──────────────────
    public async Task EnrichProviderAsync(string providerSlug, bool overwriteExisting, CancellationToken ct)
    {
        var provider = _providers.FirstOrDefault(p =>
            p.Slug.Equals(providerSlug, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            logger.LogError(
                "Provider не знайдено: '{Slug}'. Доступні: {All}",
                providerSlug,
                string.Join(", ", _providers.Select(p => p.Slug)));
            // Зменшуємо лічильник — провайдер "завершив" з помилкою
            if (state.NotifyProviderFinished())
                state.MarkFinished($"Помилка: провайдер '{providerSlug}' не знайдено.");
            return;
        }

        // Лінкуємо 3 джерела скасування:
        //   ct              — Hangfire ShutdownToken (зупинка сервера)
        //   timeoutCts      — наш жорсткий ліміт 7 год
        //   state.StopToken — кнопка "Stop" в адмін-панелі
        // Тепер Stop перериває навіть активні GetGameDetailsAsync — не лише між батчами.
        using var timeoutCts = new CancellationTokenSource(ProviderTimeout);
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(
            ct, timeoutCts.Token, state.StopToken);

        state.NotifyProviderStarted(providerSlug);
        logger.LogInformation("[{Slug}] ▶ Збагачення починається", providerSlug);

        try
        {
            await ProcessProviderInternalAsync(provider, overwriteExisting, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogError(
                "[{Slug}] ❌ Перевищено ліміт {H} год. Збільш ProviderTimeout або зменш обсяг.",
                providerSlug, ProviderTimeout.TotalHours);
            throw; // Hangfire → Failed
        }
        finally
        {
            // Останній провайдер що завершився (незалежно від успіху/помилки)
            // виставляє підсумок у state → UI показує фінальний рядок
            if (state.NotifyProviderFinished())
            {
                var summary = $"Збагачення завершено. Оброблено: {state.Processed}, Помилок: {state.Failed}";
                state.MarkFinished(summary);
                logger.LogInformation("✅ Всі провайдери завершено. {Summary}", summary);
            }
        }
    }

    // ── Legacy: всі провайдери в одному job ───────────────────────────────────
    public async Task RunEnrichmentJobAsync(string? providerSlug, bool overwriteExisting, CancellationToken ct)
    {
        if (!state.TryStart())
        {
            logger.LogWarning("Спроба подвійного запуску збагачення ігор відхилена.");
            return;
        }

        state.OverwriteExisting = overwriteExisting;
        state.ResetProgress(0, providerSlug ?? "Всі магазини", "Збагачення деталей");

        try
        {
            var activeProviders = string.IsNullOrEmpty(providerSlug)
                ? _providers
                : _providers.Where(p => p.Slug.Equals(providerSlug, StringComparison.OrdinalIgnoreCase)).ToList();

            await Task.WhenAll(activeProviders.Select(p =>
                ProcessProviderInternalAsync(p, overwriteExisting, ct)));

            state.MarkFinished("Збагачення завершено.");
        }
        catch (OperationCanceledException)
        {
            state.MarkFinished("Збагачення зупинено користувачем.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критична помилка під час збагачення");
            state.LastError = ex.Message;
            state.MarkFinished("Збагачення завершилося аварійно.");
        }
    }

    // ── Спільна логіка ────────────────────────────────────────────────────────
    private async Task ProcessProviderInternalAsync(
        IStoreProvider provider,
        bool overwriteExisting,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        var externalIds = new List<string>();
        externalIds.AddRange(await repo.GetExternalIdsByStatusAsync(provider.ShopId, GameImportStatus.Basic, ct));
        externalIds.AddRange(await repo.GetExternalIdsByStatusAsync(provider.ShopId, GameImportStatus.Fail, ct));

        if (overwriteExisting)
        {
            externalIds.AddRange(await repo.GetExternalIdsByStatusAsync(provider.ShopId, GameImportStatus.Full, ct));
        }

        if (externalIds.Count == 0)
        {
            logger.LogInformation("[{Slug}] Немає ігор для збагачення.", provider.Slug);
            return;
        }

        // ВИПРАВЛЕНО: AddToTotal використовує Interlocked.Add — thread-safe при
        // паралельному запуску кількох провайдерів через Task.WhenAll (legacy) або
        // одночасних Hangfire-job'ів.
        // БУЛО: state.Total += externalIds.Count  ← race condition (read-modify-write)
        state.AddToTotal(externalIds.Count);
        logger.LogInformation("[{Slug}] {Count} ігор для збагачення.", provider.Slug, externalIds.Count);

        const int batchSize = 50;
        for (int i = 0; i < externalIds.Count; i += batchSize)
        {
            // CT вже містить StopToken через LinkedCts — окрема перевірка
            // !state.IsRunning більше не потрібна: скасування прийде через токен.
            ct.ThrowIfCancellationRequested();

            var batch = externalIds.Skip(i).Take(batchSize).ToList();
            await EnrichBatchAsync(provider, batch, overwriteExisting, ct);
        }
    }

    private async Task EnrichBatchAsync(
        IStoreProvider provider,
        List<string> externalIds,
        bool overwriteExisting,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedRepo = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        // ── Фаза 1: паралельний fetch деталей ─────────────────────────────────
        // SemaphoreSlim видалено — ConcurrencyLimiter в Polly pipeline тепер
        // контролює скільки HTTP-запитів одночасно йде через кожен клієнт.
        // Всі 50 задач стартують відразу, але реально виконуються в межах ліміту.
        var fetchTasks = externalIds.Select(async id =>
        {
            var details = await provider.GetGameDetailsAsync(id, ct);
            return (Id: id, Details: details);
        });

        var results = await Task.WhenAll(fetchTasks);

        // ── Фаза 2: завантажуємо ігри з БД одним запитом ──────────────────────
        var successIds = results
            .Where(r => r.Details is not null)
            .Select(r => r.Id)
            .ToList();

        var games = await scopedRepo.GetGamesByExternalIdsBatchAsync(provider.ShopId, successIds, ct);
        var gamesDict = games
            .SelectMany(g => g.GameExternalIds
                .Where(e => e.ShopId == provider.ShopId)
                .Select(e => new { e.ExternalId, Game = g }))
            .ToDictionary(x => x.ExternalId, x => x.Game);

        // ── Фаза 3: маппінг + збір оновлених ігор ─────────────────────────────
        var toUpdate = new List<Game>();

        foreach (var (id, details) in results)
        {
            if (details is null || !gamesDict.TryGetValue(id, out var game)) continue;

            // overwriteExisting тепер передається напряму — не через state,
            // щоб уникнути конкурентного читання при паралельних провайдерах
            await mapper.ApplyAsync(game, details, scopedRepo, overwriteExisting, ct);
            game.ImportStatus = GameImportStatus.Full;
            game.UpdatedAt    = DateTime.UtcNow;
            toUpdate.Add(game);
            state.IncrementProcessed();
        }

        // ── Фаза 4: один SaveChanges на весь батч ─────────────────────────────
        if (toUpdate.Count > 0)
            await scopedRepo.UpdateBatchAsync(toUpdate, ct);
    }
}