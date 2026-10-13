namespace EplanMcp.Bridge.Core.Threading;

public enum DispatchStatus
{
    /// <summary>The work ran and returned.</summary>
    Completed,

    /// <summary>The work did not run; see <see cref="BusyReason"/>.</summary>
    Busy,

    /// <summary>The work ran and threw; the exception was caught on the main thread.</summary>
    Faulted,
}
