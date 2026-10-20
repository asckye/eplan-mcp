using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One invoke as the operation running it sees it (M4 design 4.3): where its progress goes. The router creates one per
/// invoke and makes it <see cref="Current"/> on the thread that runs the operation, EPLAN's main thread, which runs one
/// operation at a time; so operations reach it without a new parameter. Used only from the thread that runs the
/// operation.
/// </summary>
public sealed class OperationContext
{
    // Outside an invoke, for example an operation a test runs directly: progress goes nowhere.
    private static readonly OperationContext _none = new(string.Empty, progress: null);

    [ThreadStatic]
    private static OperationContext? _current;

    private readonly ProgressSlot? _progress;
    private int _lastProgress = int.MinValue;

    internal OperationContext(string requestId, ProgressSlot? progress)
    {
        RequestId = requestId;
        _progress = progress;
    }

    /// <summary>The context of the operation running on this thread.</summary>
    public static OperationContext Current => _current ?? _none;

    /// <summary>The request ID (<c>correlationId</c>) of the invoke.</summary>
    public string RequestId { get; }

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
