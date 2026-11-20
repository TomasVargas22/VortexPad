using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexPad.ViewModels;

public enum SaveStatus
{
    Idle,
    Pending,
    Saving,
    Saved,
    Failed
}

/// <summary>
/// Debounced autosave. <see cref="MarkDirty"/> after every edit; once <c>delay</c> passes without new edits,
/// the snapshot is taken on the calling thread (the UI one, so the profile is not read while it is edited)
/// and persisted on the thread pool. <see cref="Flush"/> saves synchronously what is pending (on close or
/// before switching profile). Never used from the 1000 Hz loop.
/// </summary>
public sealed class Autosaver<T> : ObservableObject
{
    private readonly Func<T> _snapshot;
    private readonly Action<T> _persist;
    private readonly TimeSpan _delay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _persistLock = new();
    private CancellationTokenSource? _pending;
    private SaveStatus _status;
    private string? _lastError;

    public Autosaver(Func<T> snapshot, Action<T> persist, TimeSpan delay,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _delay = delay;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public SaveStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Message of the last failed save (null after a successful one).</summary>
    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public bool HasPendingChanges => _pending != null;

    /// <summary>Task of the last scheduled save (tests and diagnostics).</summary>
    public Task LastRun { get; private set; } = Task.CompletedTask;

    public void MarkDirty()
    {
        _pending?.Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;
        Status = SaveStatus.Pending;
        LastRun = RunAsync(cts);
    }

    /// <summary>Saves the pending change right now on the calling thread. Returns false if it failed.</summary>
    public bool Flush()
    {
        if (_pending == null) return true;
        _pending.Cancel();
        _pending = null;
        return SaveNow(_snapshot());
    }

    private async Task RunAsync(CancellationTokenSource cts)
    {
        try
        {
            await _delayAsync(_delay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested || !ReferenceEquals(_pending, cts)) return;

        _pending = null;
        T snapshot = _snapshot();
        Status = SaveStatus.Saving;
        try
        {
            await Task.Run(() => Persist(snapshot));
            LastError = null;
            if (_pending == null) Status = SaveStatus.Saved;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Status = SaveStatus.Failed;
        }
    }

    private bool SaveNow(T snapshot)
    {
        try
        {
            Persist(snapshot);
            LastError = null;
            Status = SaveStatus.Saved;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Status = SaveStatus.Failed;
            return false;
        }
    }

    private void Persist(T snapshot)
    {
        // A background save and a Flush must not write the same file at once
        lock (_persistLock) _persist(snapshot);
    }
}
