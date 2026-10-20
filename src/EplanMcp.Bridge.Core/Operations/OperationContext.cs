using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One invoke as the operation running it sees it (M4 design 4.3): whether the Host asked to cancel it, and where its
/// progress goes. The router creates one per invoke and makes it <see cref="Current"/> on the thread that runs the
/// operation, EPLAN's main thread, which runs one operation at a time; so operations and <see cref="TransactionRunner"/>
/// reach it without a new parameter. <see cref="Cancel"/> comes from a pipe thread, everything else from the thread that
/// runs the operation.
/// </summary>
public sealed class OperationContext
{
    // Outside an invoke, for example an operation a test runs directly: never cancelled, and progress goes nowhere.
    private static readonly OperationContext _none = new(string.Empty, write: false, progress: null);

    [ThreadStatic]
    private static OperationContext? _current;

    private readonly bool _write;
    private readonly ProgressSlot? _progress;
    private int _cancelled;
    private int _lastProgress = int.MinValue;

    internal OperationContext(string requestId, bool write, ProgressSlot? progress)
    {
        RequestId = requestId;
        _write = write;
        _progress = progress;
    }

    /// <summary>The context of the operation running on this thread.</summary>
    public static OperationContext Current => _current ?? _none;

    /// <summary>The request ID (<c>correlationId</c>) of the invoke.</summary>
    public string RequestId { get; }

    public bool IsCancellationRequested => Volatile.Read(ref _cancelled) != 0;

    /// <summary>
    /// A safe point: throws CANCELLED when the Host has asked to cancel. Call it only where stopping leaves nothing half
    /// done: before each step and after an EPLAN call has returned, never during one. The error of a write says
    /// not_started; inside its transaction <see cref="TransactionRunner"/> rolls the change back and says rolled_back.
    /// </summary>
    public void ThrowIfCancellationRequested()
    {
        if (IsCancellationRequested)
        {
            var error = new BridgeErrorException(ErrorReason.Cancelled, "The call was cancelled at the caller's request before it finished; the project was not changed.");
            error.Error.Execution = _write ? ExecutionState.NotStarted : null;
            throw error;
        }
    }

    /// <summary>
    /// Reports <paramref name="progress"/> steps done of <paramref name="total"/>, when known. Only the newest report is
    /// sent, at a limited rate; one below an earlier one is ignored, because progress never goes back.
    /// <paramref name="message"/> is a short English phrase without project content (<see cref="ProgressNotification.Message"/>).
    /// </summary>
    public void ReportProgress(int progress, int? total = null, string? message = null)
    {
        if (_progress is null || progress < _lastProgress)
        {
            return;
        }

        _lastProgress = progress;
        _progress.Post(new ProgressNotification { RequestId = RequestId, Progress = progress, Total = total, Message = message });
    }

    /// <summary>Flags the invoke; it stops at its next safe point. Any thread.</summary>
    internal void Cancel() => Interlocked.Exchange(ref _cancelled, 1);

    /// <summary>Runs <paramref name="work"/> with this context as <see cref="Current"/> on the calling thread.</summary>
    internal object? Run(Func<object?> work)
    {
        OperationContext? previous = _current;
        _current = this;
        try
        {
            return work();
        }
        finally
        {
            _current = previous;
        }
    }
}
