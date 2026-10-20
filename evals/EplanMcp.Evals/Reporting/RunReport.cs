using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Reporting;

/// <summary><c>solved</c> and <c>failed</c> count towards the success rate; <c>invalid</c> does not (design 4.2).</summary>
internal static class Outcome
{
    public const string Solved = "solved";
    public const string Failed = "failed";
    public const string Invalid = "invalid";
}

/// <summary>
/// One attempt as the report keeps it (agent evals design 4.3). <see cref="Failures"/> are the failed checks of a graded
/// answer; <see cref="ToolCalls"/> counts the calls by tool; <see cref="ErrorCalls"/> returned an error, and after
/// <see cref="Corrected"/> of them the model called the same tool again and succeeded. Input tokens include the cache.
/// <see cref="Retry"/> marks the second run of an attempt that was invalid before the model did anything.
/// </summary>
internal sealed record AttemptRecord(
    int Attempt,
    string Outcome,
    string? Reason,
    IReadOnlyList<string> Failures,
    IReadOnlyDictionary<string, int> ToolCalls,
    int ErrorCalls,
    int Corrected,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    int Turns,
    double Seconds,
    bool Retry = false)
{
    /// <summary>Joins what claude did with how its answer graded; <paramref name="verdict"/> is null when nothing was graded.</summary>
    public static AttemptRecord From(int attempt, AgentAttempt run, Verdict? verdict, bool retry = false)
    {
        (string outcome, string? reason, IReadOnlyList<string> failures) = (run.Status, verdict) switch
        {
            (AttemptStatus.Answered, { Invalid.Count: > 0 } v) => (Reporting.Outcome.Invalid, string.Join("; ", v.Invalid), (IReadOnlyList<string>)[]),
            (AttemptStatus.Answered, { Failures.Count: > 0 } v) => (Reporting.Outcome.Failed, "the answer failed checks", v.Failures),
            (AttemptStatus.Answered, _) => (Reporting.Outcome.Solved, null, []),
            (AttemptStatus.Failed, _) => (Reporting.Outcome.Failed, run.Reason, []),
            _ => (Reporting.Outcome.Invalid, run.Reason, []),
        };
        AgentTrace trace = run.Trace;
        TokenUsage? usage = trace.Result?.Usage;
        return new AttemptRecord(
            attempt,
            outcome,
            reason,
            failures,
            trace.Calls.GroupBy(c => c.Tool).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            trace.Calls.Count(c => c.IsError == true),
            trace.Corrected,
            usage is null ? 0 : usage.Input + usage.CacheRead + usage.CacheCreation,
            usage?.Output ?? 0,
            trace.Result?.CostUsd ?? 0,
            trace.Result?.Turns ?? 0,
            Math.Round(run.Elapsed.TotalSeconds, 1),
            retry);
    }
}

/// <summary>A task in the report: <see cref="Invalid"/> is set when its oracle calls failed, so no attempt was made.</summary>
internal sealed record TaskRecord(string Id, string Set, string? Invalid, IReadOnlyList<string> Notes, IReadOnlyList<AttemptRecord> Attempts);

/// <summary>Everything one run produced (report.json); report.md summarizes it.</summary>
internal sealed record RunReport(
    DateTimeOffset Started,
    string Model,
    string Claude,
    string Eplanmcp,
    IReadOnlyList<string> InvalidFiles,
    IReadOnlyList<TaskRecord> Tasks);
