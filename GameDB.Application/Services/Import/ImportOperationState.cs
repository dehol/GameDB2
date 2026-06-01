using System;
using System.Threading;

namespace GameDB.Application.Services.Import;

/// <summary>
/// Стан довготривалої фонової операції (збагачення або синхронізація цін).
/// Числові лічильники thread-safe через Interlocked — коректні при паралельному
/// запуску кількох провайдерів одночасно.
/// </summary>
public class ImportOperationState
{
    private volatile bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        set => _isRunning = value;
    }

    // FIX: Interlocked замість auto-property — thread-safe при паралельних провайдерах
    private int _processed;
    private int _total;

    public int Processed
    {
        get => Volatile.Read(ref _processed);
        set => Volatile.Write(ref _processed, value);
    }

    public int Total
    {
        get => Volatile.Read(ref _total);
        set => Volatile.Write(ref _total, value);
    }

    /// <summary>Thread-safe інкремент — викликається з паралельних потоків провайдерів.</summary>
    public int IncrementProcessed() => Interlocked.Increment(ref _processed);

    // Решта полів — пишуться/читаються лише з одного потоку worker'а
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
        IsRunning   = false;
        FinishedAt  = DateTime.UtcNow;
        LastMessage = message;
    }
}
