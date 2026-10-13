using System.Diagnostics;
using Eplan.EplApi.Base.Internal;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Debug-build guard for adapter entry points (coding standards 9.2, rule 4).</summary>
internal static class MainThread
{
    /// <summary><c>CanAccessMainThread</c> is true only on EPLAN's main thread (api-notes A9).</summary>
    [Conditional("DEBUG")]
    public static void Assert() =>
        Debug.Assert(EplanMainThreadDispatcher.CanAccessMainThread(), "EPLAN API called off the main thread; use the dispatcher.");
}
