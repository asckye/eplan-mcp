using System.Text.Json.Nodes;

namespace EplanMcp.Evals.Agent;

/// <summary>
/// <see cref="Answered"/> attempts go on to grading. <see cref="Failed"/> ones count against the model (no answer,
/// over budget); <see cref="Invalid"/> ones do not count at all, because the environment failed (agent evals design 4.2).
/// </summary>
internal enum AttemptStatus
{
    Answered,
    Failed,
    Invalid,
}

/// <summary>How the runner stopped claude before it finished, and how that counts.</summary>
internal sealed record AgentStop(AttemptStatus Status, string Reason);

/// <summary>
/// One run of <c>claude -p</c> on one task, with the trace the metrics come from. <see cref="Elapsed"/> is measured by
/// the runner, so attempts it stopped have a time too.
/// </summary>
internal sealed record AgentAttempt(AttemptStatus Status, string? Reason, JsonNode? Answer, AgentTrace Trace, TimeSpan Elapsed)
{
    private const int StderrShown = 300;

    // The model's own doing, unlike the other assistant errors (login, rate limit, overload, server).
    private const string OutputLimitError = "max_output_tokens";

    // No login: waiting does not fix it, unlike the other API errors.
    private const string LoginError = "authentication_failed";

    /// <summary>
    /// Invalid because of an API error before the model did anything: no tool call, nothing spent. The runner retries
    /// such an attempt once. On 2026-10-19 another Claude Code process refreshing the shared login made six attempts in
    /// a row invalid ("Failed to refresh OAuth token ... retry in a minute"); a missing login is not retried.
    /// </summary>
    public bool IsRetryable =>
        Status == AttemptStatus.Invalid
        && (Trace.ApiError is not null || Trace.Result?.TerminalReason == "api_error")
        && Trace.ApiError != LoginError
        && Trace.Calls.Count == 0
        && (Trace.Result?.CostUsd ?? 0) == 0;

    public static AgentAttempt From(AgentTrace trace, AgentStop? stop, int exitCode, string stderr, TimeSpan elapsed)
    {
        (AttemptStatus status, string? reason, JsonNode? answer) = Judge(trace, stop, exitCode, stderr);
        return new(status, reason, answer, trace, elapsed);
    }

    private static (AttemptStatus Status, string? Reason, JsonNode? Answer) Judge(AgentTrace trace, AgentStop? stop, int exitCode, string stderr)
    {
        if (stop is not null)
        {
            return (stop.Status, stop.Reason, null);
        }

        if (trace.Result is not { } result)
        {
            string tail = stderr.Trim() is { Length: > 0 } text ? ": " + (text.Length > StderrShown ? "..." + text[^StderrShown..] : text) : string.Empty;
            return (AttemptStatus.Invalid, $"claude exited with code {exitCode} and no result{tail}", null);
        }

        // An answer counts even after an error the run recovered from.
        if (result is { IsError: false, Answer: { } answer })
        {
            return (AttemptStatus.Answered, null, answer);
        }

        return result switch
        {
            _ when trace.ApiError == OutputLimitError => (AttemptStatus.Failed, $"over the output limit: {result.Message}", null),
            _ when trace.ApiError is not null || result.TerminalReason == "api_error" =>
                (AttemptStatus.Invalid, $"API error {trace.ApiError ?? "(unknown)"}: {result.Message}", null),
            { Subtype: "error_max_budget_usd" } => (AttemptStatus.Failed, $"over the spending limit: {result.Message}", null),
            { Subtype: "error_max_turns" } => (AttemptStatus.Failed, $"out of turns: {result.Message}", null),
            _ => (AttemptStatus.Failed, $"no structured answer ({result.Subtype}): {result.Message}", null),
        };
    }
}
