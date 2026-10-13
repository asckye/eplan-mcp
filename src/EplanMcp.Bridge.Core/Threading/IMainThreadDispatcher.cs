namespace EplanMcp.Bridge.Core.Threading;

/// <summary>
/// Runs work on EPLAN's main thread, the only thread allowed to call the EPLAN API (api-notes B6).
/// Implementations must follow the S1 rules: never wait without a timeout, never let an exception escape on the
/// main thread (it terminates EPLAN), refuse while a modal dialog is open, and refuse re-entrant calls.
/// </summary>
public interface IMainThreadDispatcher
{
    /// <summary>
    /// Runs <paramref name="work"/> on the main thread and blocks the calling (background) thread until it is done.
    /// If the main thread does not pick the work up within <paramref name="startTimeout"/>, the work is abandoned
    /// and never runs later.
    /// </summary>
    public DispatchResult Run(Func<object?> work, TimeSpan startTimeout);
}
