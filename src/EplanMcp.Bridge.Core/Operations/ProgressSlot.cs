using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The newest progress report of one invoke (M4 design 4.3): posted by the operation on EPLAN's main thread, taken by
/// the connection's listener, which sends it. Posting never waits for the pipe: <see cref="Changed"/> runs its
/// continuations on the thread pool, never on the posting thread. Thread-safe; <see cref="_gate"/> guards both fields.
/// </summary>
internal sealed class ProgressSlot
{
    private readonly object _gate = new();
    private ProgressNotification? _latest;
    private TaskCompletionSource<bool> _changed = NewSignal();

    /// <summary>Completes when a report has been posted since the last <see cref="Take"/>; may also complete with none.</summary>
    public Task Changed
    {
        get
        {
            lock (_gate)
            {
                return _changed.Task;
            }
        }
    }

    /// <summary>Replaces the report not taken yet, if any.</summary>
    public void Post(ProgressNotification report)
    {
        TaskCompletionSource<bool> changed;
        lock (_gate)
        {
            _latest = report;
            changed = _changed;
        }

        changed.TrySetResult(true);
    }

    /// <summary>The newest report not taken yet; null when there is none.</summary>
    public ProgressNotification? Take()
    {
        lock (_gate)
        {
            ProgressNotification? latest = _latest;
            _latest = null;
            if (_changed.Task.IsCompleted)
            {
                _changed = NewSignal();
            }

            return latest;
        }
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
