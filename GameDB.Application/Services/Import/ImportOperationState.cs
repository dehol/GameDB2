using System;
using System.Threading;

namespace GameDB.Application.Services.Import;

public class ImportOperationState
{
    private volatile bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        set => _isRunning = value;
    }

    public string?   CurrentProvider { get; set; }
    public string?   CurrentPhase    { get; set; }
    public int       Processed       { get; set; }
    public int       Total           { get; set; }
    public int       BatchSize       { get; set; }
    public string?   LastMessage     { get; set; }
    public string?   LastError       { get; set; }
    public DateTime? StartedAt       { get; set; }
    public DateTime? FinishedAt      { get; set; }
    public bool      OverwriteExisting { get; set; }
    public int       OverwriteSkip   { get; set; }

    public CancellationTokenSource? Cts { get; set; }

    public void ResetProgress(int total, string provider, string phase)
    {
        Processed       = 0;
        Total           = total;
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
