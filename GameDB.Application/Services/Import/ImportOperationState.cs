namespace GameDB.Application.Services.Import;

/// <summary>
/// Стан довготривалої фонової операції (збагачення або синхронізація цін).
/// Числові лічильники thread-safe через Interlocked — коректні при паралельному
/// запуску кількох провайдерів одночасно.
/// </summary>
public class ImportOperationState
{
    // ── IsRunning ─────────────────────────────────────────────────────────────
    // 0 = false, 1 = true — Interlocked потребує int/long
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>
    /// Атомарний запуск. Повертає true якщо операцію вдалося запустити
    /// (раніше не була активна). Захищає від подвійного запуску.
    /// </summary>
    public bool TryStart()
        => Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;

    public void ForceSetRunning(bool value)
        => Interlocked.Exchange(ref _isRunning, value ? 1 : 0);

    // ── StopCts: скасування через кнопку "Stop" ───────────────────────────────
    // _stopCts — замінюється при кожному новому запуску (ResetStop).
    // StopToken лінкується у PriceSyncService.SyncProviderAsync разом з
    // Hangfire ShutdownToken і жорстким таймаутом.
    // Volatile.Read/Write для reference type — атомарно на x64, але
    // explicit volatile гарантує видимість між потоками.
    private volatile CancellationTokenSource _stopCts = new();

    /// <summary>
    /// Токен, який скасовується при виклику RequestStop().
    /// Лінкується в SyncProviderAsync для паралельних Hangfire-job-ів.
    /// </summary>
    public CancellationToken StopToken => _stopCts.Token;

    /// <summary>
    /// Зупиняє поточну операцію:
    ///   — встановлює IsRunning = false (для legacy-перевірок у UI)
    ///   — скасовує StopToken (реально зупиняє Hangfire-job через LinkedCts)
    /// </summary>
    public void RequestStop()
    {
        Interlocked.Exchange(ref _isRunning, 0);
        _stopCts.Cancel();
    }

    /// <summary>
    /// Викликається перед новим запуском (AdminService.StartPriceSync).
    /// Замінює скасований CTS на свіжий, щоб StopToken не був вже скасований.
    /// </summary>
    public void ResetStop()
    {
        var fresh = new CancellationTokenSource();
        var old   = Interlocked.Exchange(ref _stopCts, fresh);
        // Dispose тільки якщо вже скасований, інакше GC сам прибере
        if (old.IsCancellationRequested)
            old.Dispose();
    }

    // ── Лічильники (Interlocked — паралельні провайдери) ─────────────────────
    private int _processed;
    private int _total;
    private int _failed;
    private int _activeProviders; // скільки провайдерів ще працює

    public int Processed => Volatile.Read(ref _processed);
    public int Total
    {
        get => Volatile.Read(ref _total);
        set => Volatile.Write(ref _total, value);
    }
    public int Failed => Volatile.Read(ref _failed);

    public int IncrementProcessed() => Interlocked.Increment(ref _processed);
    public int IncrementFailed()    => Interlocked.Increment(ref _failed);

    // Викликається з провайдера коли він дізнається скільки ігор у нього є
    public void AddToTotal(int count) => Interlocked.Add(ref _total, count);

    // ── Метадані (пишуться/читаються з одного потоку worker'а) ───────────────
    public string?   CurrentProvider   { get; set; }
    public string?   CurrentPhase      { get; set; }
    public int       BatchSize         { get; set; }
    public string?   LastMessage       { get; set; }
    public string?   LastError         { get; set; }
    public DateTime? StartedAt         { get; set; }
    public DateTime? FinishedAt        { get; set; }
    public bool      OverwriteExisting { get; set; }
    public int       OverwriteSkip     { get; set; }

    public void ResetProgress(int total, string provider, string phase)
    {
        Volatile.Write(ref _processed, 0);
        Volatile.Write(ref _total,     total);
        Volatile.Write(ref _failed,    0);
        BatchSize       = 0;
        LastMessage     = null;
        LastError       = null;
        StartedAt       = DateTime.UtcNow;
        FinishedAt      = null;
        CurrentProvider = provider;
        CurrentPhase    = phase;
    }

    /// <summary>
    /// Викликається з AdminService перед постановкою job-ів у чергу.
    /// Одразу виставляє IsRunning = true → UI показує прогрес без затримки.
    /// providerCount — скільки паралельних job-ів буде запущено.
    /// </summary>
    public void PrepareParallelSync(string phase, int providerCount)
    {
        Volatile.Write(ref _processed,       0);
        Volatile.Write(ref _total,           0); // буде наповнюватись через AddToTotal
        Volatile.Write(ref _failed,          0);
        Volatile.Write(ref _activeProviders, providerCount);
        Interlocked.Exchange(ref _isRunning, 1); // IsRunning = true одразу
        StartedAt       = DateTime.UtcNow;
        FinishedAt      = null;
        CurrentPhase    = phase;
        CurrentProvider = "Всі провайдери";
        LastMessage     = "Запуск синхронізації...";
        LastError       = null;
    }

    /// <summary>
    /// Викликається на початку кожного provider-job-а.
    /// Оновлює CurrentProvider для UI (race condition між потоками прийнятний —
    /// це тільки рядок для відображення, не критичні дані).
    /// </summary>
    public void NotifyProviderStarted(string providerSlug)
        => CurrentProvider = providerSlug;

    /// <summary>
    /// Викликається у finally кожного provider-job-а.
    /// Повертає true якщо це був останній активний провайдер → час викликати MarkFinished.
    /// </summary>
    public bool NotifyProviderFinished()
        => Interlocked.Decrement(ref _activeProviders) <= 0;

    public void MarkFinished(string message)
    {
        Interlocked.Exchange(ref _isRunning, 0);
        FinishedAt  = DateTime.UtcNow;
        LastMessage = message;
    }
}

public sealed class EnrichmentOperationState  : ImportOperationState { }
public sealed class PriceSyncOperationState   : ImportOperationState { }
public sealed class BasicImportOperationState : ImportOperationState { }