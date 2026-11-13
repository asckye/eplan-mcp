using System.Diagnostics;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Observes caught native failures only while a write scope is open; never changes error handling.</summary>
public static class WriteExceptionDiagnostics
{
    [ThreadStatic] private static List<WeakReference<Exception>>? _seen;

    internal static IDisposable Begin() => new Scope();

    public static void Report(Exception failure)
    {
        if (_seen is null)
        { return; }
        if (failure is AggregateException aggregate)
        { foreach (Exception inner in aggregate.InnerExceptions) { Report(inner); } }
        else if (failure.InnerException is { } inner)
        { Report(inner); }
        if ((failure.GetType().Namespace?.StartsWith("Eplan.EplApi.", StringComparison.Ordinal) == true
            || failure is ApplicationException
            || new StackTrace(failure).GetFrames()?.Any(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("Eplan.EplApi.", StringComparison.Ordinal) == true) == true)
            && !_seen.Any(reference => reference.TryGetTarget(out Exception? previous) && ReferenceEquals(previous, failure)))
        {
            _seen.Add(new WeakReference<Exception>(failure));
            // The type alone: an EPLAN message is untrusted text, while the type names which native call failed.
            OperationContext.Current.Warn("native exception inside a write (" + failure.GetType().Name + "); EPLAN state may be unstable");
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly List<WeakReference<Exception>>? _previous = _seen;
        internal Scope() => _seen ??= [];
        public void Dispose() => _seen = _previous;
    }
}
