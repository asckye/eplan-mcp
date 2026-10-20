using System.Diagnostics;
using System.Windows.Threading;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Debug-build guard for adapter entry points (coding standards 9.2, rule 4).</summary>
internal static class MainThread
{
    private static Dispatcher? _main;

    /// <summary>Set once in OnInitGui, on the main thread, before any adapter runs (<see cref="EplanDispatcher.CaptureOnMainThread"/>).</summary>
    public static void Use(Dispatcher main) => _main = main;

    /// <summary>
    /// The main dispatcher's own <c>CheckAccess</c>, which every EPLAN version has; on 2026 it agrees with
    /// <c>EplanMainThreadDispatcher.CanAccessMainThread</c> (api-notes A9, ADR-0009).
    /// </summary>
    [Conditional("DEBUG")]
    public static void Assert() =>
        Debug.Assert(_main?.CheckAccess() == true, "EPLAN API called off the main thread; use the dispatcher.");
}
