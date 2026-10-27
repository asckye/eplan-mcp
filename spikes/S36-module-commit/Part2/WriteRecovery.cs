using System.Diagnostics;

namespace EplanMcp.Spikes.S36.Part2;

// No native scopes here. The caller owns the single write chain; log the original BEFORE any recovery action.
internal static class WriteRecovery
{
    internal static void Rollback(Exception original, string stage, Action prepare, Action rollback, Action end,
        Action abandon, Action<string, object> log)
    {
        Log(log, "write_error", new { stage, error = Evidence.Error(original) });
        Exception? cleanupError = null;
        try { prepare(); }
        catch (Exception ex)
        {
            cleanupError = ex;
            Log(log, "rollback_cleanup_failed", new { stage, error = Evidence.Error(ex) });
        }
        try { rollback(); }
        catch (Exception ex)
        {
            // No EndWrite/readback/second rollback/disposal of native handles is safe after this boundary.
            abandon();
            Log(log, "rollback_failed", new { stage, recoveryStage = "safety.rollback", outcome = "unknown",
                instruction = "Stop. Re-copy the closed scratch project and restart EPLAN before another run.",
                original = Evidence.Error(original), cleanup = cleanupError is null ? null : Evidence.Error(cleanupError), error = Evidence.Error(ex) });
            throw new OutcomeUnknownException(original, ex, cleanupError);
        }
        if (cleanupError is not null)
        {
            abandon();
            Log(log, "rollback_cleanup_unknown", new { stage, outcome = "unknown", error = Evidence.Error(cleanupError) });
            throw new OutcomeUnknownException(original, cleanupError);
        }
        end();
    }
    internal static void Log(Action<string, object> log, string phase, object data)
    {
        try { log(phase, data); }
        catch (Exception ex) { Trace.TraceError("S36P2 {0} log failed: {1}; evidence: {2}", phase, ex, data); }
    }
    internal static void Quarantine(params object?[] handles)
    {
#pragma warning disable CA1816 // Unknown native state forbids Dispose and deferred finalizer recovery; the caller owns these handles.
        foreach (object? handle in handles) { if (handle is not null) { GC.SuppressFinalize(handle); } }
#pragma warning restore CA1816
    }
    internal sealed class OutcomeUnknownException : Exception
    {
        internal OutcomeUnknownException(Exception original, Exception recovery, Exception? cleanup = null)
            : base("S36P2 outcome unknown. Stop; re-copy the closed scratch project and restart EPLAN.",
                new AggregateException(cleanup is null ? new[] { original, recovery } : new[] { original, recovery, cleanup })) { }
    }
}
