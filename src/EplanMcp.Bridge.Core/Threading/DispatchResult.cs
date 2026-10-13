namespace EplanMcp.Bridge.Core.Threading;

/// <summary>Outcome of <see cref="IMainThreadDispatcher.Run"/>: exactly one of completed, busy or faulted.</summary>
public sealed class DispatchResult
{
    private DispatchResult(DispatchStatus status, object? value, BusyReason busyReason, Exception? exception)
    {
        Status = status;
        Value = value;
        BusyReason = busyReason;
        Exception = exception;
    }

    public DispatchStatus Status { get; }

    /// <summary>The work's return value when <see cref="Status"/> is <see cref="DispatchStatus.Completed"/>.</summary>
    public object? Value { get; }

    /// <summary>Why the work did not run when <see cref="Status"/> is <see cref="DispatchStatus.Busy"/>.</summary>
    public BusyReason BusyReason { get; }

    /// <summary>What the work threw on the main thread when <see cref="Status"/> is <see cref="DispatchStatus.Faulted"/>.</summary>
    public Exception? Exception { get; }

    public static DispatchResult Completed(object? value) => new(DispatchStatus.Completed, value, BusyReason.None, null);

    public static DispatchResult Busy(BusyReason reason) => new(DispatchStatus.Busy, null, reason, null);

    public static DispatchResult Faulted(Exception exception) => new(DispatchStatus.Faulted, null, BusyReason.None, exception);
}
