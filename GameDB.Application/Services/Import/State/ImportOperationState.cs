namespace GameDB.Application.Services.Import;

/// <summary>
/// Стан довготривалої фонової операції (збагачення або синхронізація цін).
/// Числові лічильники thread-safe через Interlocked — коректні при паралельному
/// запуску кількох провайдерів одночасно.
/// </summary>
public class ImportOperationState
{
    // 0 = false, 1 = true — Interlocked потребує int
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>
    /// Атомарний запуск. Повертає true якщо операцію вдалося запустити
    /// (раніше не була активна). Захищає від подвійного запуску.
    /// </summary>
    public bool TryStart()
        => Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;

    // _stopCts замінюється при кожному новому запуску (ResetStop).
    // volatile гарантує видимість нового посилання між потоками після Interlocked.Exchange.
    private volatile CancellationTokenSource _stopCts = new();

    /// <summary>
    /// Токен, який скасовується при виклику RequestStop().
    /// Лінкується в SyncProviderAsync / EnrichProviderAsync для зупинки через UI.
    /// </summary>
    public CancellationToken StopToken => _stopCts.Token;

    /// <summary>
    /// Зупиняє поточну операцію: скасовує StopToken і виставляє IsRunning = false.
    /// </summary>
    public void RequestStop()
    {
        Interlocked.Exchange(ref _isRunning, 0);
        _stopCts.Cancel();
    }

    /// <summary>
    /// Викликається перед новим запуском. Замінює CTS на свіжий,
    /// щоб StopToken не був вже скасований від попереднього Stop.
    /// </summary>
    public void ResetStop()
    {
        var old = Interlocked.Exchange(ref _stopCts, new CancellationTokenSource());
        old.Dispose();
    }

    // ── Лічильники (Interlocked — паралельні провайдери) ─────────────────────

    private int _processed;
    private int _total;
    private int _failed;
    private int _activeProviders;

    public int Processed => Volatile.Read(ref _processed);
    public int Total     => Volatile.Read(ref _total);
    public int Failed    => Volatile.Read(ref _failed);

    public int  IncrementProcessed()           => Interlocked.Increment(ref _processed);
    public int  IncrementFailed()              => Interlocked.Increment(ref _failed);
    public int  AddFailed(int count)              => Interlocked.Add(ref _failed, count);
    public void AddToTotal(int count)          => Interlocked.Add(ref _total,     count);
    public void AddToProcessed(int count)      => Interlocked.Add(ref _processed, count);

    // ── Метадані ─────────────────────────────────────────────────────────────

    public string?   CurrentProvider   { get; set; }
    public string?   CurrentPhase      { get; set; }
    public int       BatchSize         { get; set; }
    public string?   LastMessage       { get; set; }
    public string?   LastError         { get; set; }
    public DateTime? StartedAt         { get; set; }
    public DateTime? FinishedAt        { get; set; }
    public bool      OverwriteExisting { get; set; }

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
    /// Total = 0: буде наповнюватись через AddToTotal з кожного провайдера.
    /// </summary>
    public void PrepareParallelSync(string phase, int providerCount)
    {
        Volatile.Write(ref _processed,       0);
        Volatile.Write(ref _total,           0);
        Volatile.Write(ref _failed,          0);
        Volatile.Write(ref _activeProviders, providerCount);
        Interlocked.Exchange(ref _isRunning, 1);
        StartedAt       = DateTime.UtcNow;
        FinishedAt      = null;
        CurrentPhase    = phase;
        CurrentProvider = "Всі провайдери";
        LastMessage     = "Запуск синхронізації...";
        LastError       = null;
    }

    /// <summary>
    /// Оновлює CurrentProvider для UI. Race condition між потоками прийнятний —
    /// це лише рядок для відображення, не критичні дані.
    /// </summary>
    public void NotifyProviderStarted(string providerSlug)
        => CurrentProvider = providerSlug;

    /// <summary>
    /// Викликається у finally кожного provider-job-а.
    /// Повертає true якщо це був останній активний провайдер.
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
