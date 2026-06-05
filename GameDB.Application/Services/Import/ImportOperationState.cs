using System.Threading;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Стан довготривалої фонової операції (збагачення або синхронізація цін).
/// Числові лічильники thread-safe через Interlocked — коректні при паралельному
/// запуску кількох провайдерів одночасно.
///
/// FIX: IsRunning — volatile bool + set через value дозволяло race condition
/// між перевіркою і встановленням. Тепер: int-поле + Interlocked.Exchange (0/1).
/// Setter зберіжено для зворотної сумісності, але MarkFinished/Start — безпечні.
/// </summary>
public class ImportOperationState
{
    // 0 = false, 1 = true — Interlocked потребує int/long
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>
    /// Атомарний запуск. Повертає true, якщо операцію вдалося запустити
    /// (раніше не була активна). Захищає від подвійного запуску.
    /// </summary>
    public bool TryStart()
        => Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0;

    /// <summary>Атомарна зупинка (thread-safe).</summary>
    public void RequestStop()
        => Interlocked.Exchange(ref _isRunning, 0);

    // Зворотна сумісність — AdminService може писати IsRunning = true/false
    // безпосередньо (рідко, не у паралельних контекстах).
    public void ForceSetRunning(bool value)
        => Interlocked.Exchange(ref _isRunning, value ? 1 : 0);

    // ── Лічильники (Interlocked — паралельні провайдери) ──────────────────
    private int _processed;
    private int _total;
    private int _failed;

    public int Processed => Volatile.Read(ref _processed);
    public int Total
    {
        get => Volatile.Read(ref _total);
        set => Volatile.Write(ref _total, value);
    }
    public int Failed => Volatile.Read(ref _failed);

    public int IncrementProcessed() => Interlocked.Increment(ref _processed);
    public int IncrementFailed()    => Interlocked.Increment(ref _failed);

    // ── Поля-метадані (пишуться/читаються з одного потоку worker'а) ────────
    public string?   CurrentProvider   { get; set; }
    public string?   CurrentPhase      { get; set; }
    public int       BatchSize         { get; set; }
    public string?   LastMessage       { get; set; }
    public string?   LastError         { get; set; }
    public DateTime? StartedAt         { get; set; }
    public DateTime? FinishedAt        { get; set; }
    public bool      OverwriteExisting { get; set; }
    public int       OverwriteSkip     { get; set; }

    public CancellationTokenSource? Cts { get; set; }

    public void ResetProgress(int total, string provider, string phase)
    {
        Volatile.Write(ref _processed, 0);
        Volatile.Write(ref _total, total);
        Volatile.Write(ref _failed, 0);
        BatchSize       = 0;
        LastMessage     = null;
        LastError       = null;
        StartedAt       = DateTime.UtcNow;
        FinishedAt      = null;
        CurrentProvider = provider;
        CurrentPhase    = phase;
    }

    public void MarkFinished(string message)
    {
        RequestStop();
        FinishedAt  = DateTime.UtcNow;
        LastMessage = message;
    }
}
