using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Reporting;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Run;

/// <summary>
/// One attempt of a task by the model; <see cref="ClaudeAttempts"/> runs <c>claude -p</c>. <c>retry</c> marks the second
/// run of an attempt that came out <see cref="AgentAttempt.IsRetryable"/>.
/// </summary>
internal interface IAttemptRunner
{
    public Task<AgentAttempt> RunAsync(EvalTask task, int attempt, bool retry, CancellationToken cancellationToken);
}

/// <summary>Runs the tasks one after another (agent evals design 4.2, steps 2 to 5).</summary>
internal static class EvalRun
{
    /// <summary>How long to wait before retrying an attempt; the OAuth refresh error says "retry in a minute".</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private const string NotIsolated =
        "not isolated: eplanmcp committed a write that the evals' audit log does not show, so it did not use the evals' data root and its write protection; check the fixture";

    /// <summary>
    /// Appends one record per task to <paramref name="records"/> as soon as the task starts, so that a cancelled run
    /// still reports what it finished. A task whose oracle calls fail gets no attempt; the after calls of an answered
    /// attempt go to the grading with its answer. An attempt that is
    /// <see cref="AgentAttempt.IsRetryable"/> runs once more after <paramref name="retryDelay"/>; both are reported, and
    /// the invalid one does not count, as always. <paramref name="auditedWrites"/> counts the writes in the audit log of
    /// the model's eplanmcp (<see cref="EvalsHome.AuditedWrites"/>, -1 when it cannot tell): an attempt with a committed
    /// write that the count does not show is invalid, because that eplanmcp did not use the evals' data root and its
    /// write protection, and the run stops after reporting it.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<EvalTask> tasks,
        int repeat,
        IToolClient client,
        IReadOnlySet<string> tools,
        IAttemptRunner runner,
        TextWriter progress,
        List<TaskRecord> records,
        TimeSpan retryDelay,
        Func<int> auditedWrites,
        CancellationToken cancellationToken)
    {
        foreach (EvalTask task in tasks)
        {
            string set = task.Set == TaskSet.Dev ? "dev" : "holdout";
            OracleValues oracle = task.Fixture == TaskFixture.Shared
                ? await OracleRunner.RunAsync(task, client, tools, null, cancellationToken).ConfigureAwait(false)
                : new OracleValues(new Dictionary<string, JsonNode?>(), ["this runner cannot make copies yet"], []);
            if (oracle.Problems.Count > 0)
            {
                string problems = string.Join("; ", oracle.Problems);
                records.Add(new TaskRecord(task.Id, set, problems, oracle.Notes, []));
                await progress.WriteLineAsync($"{task.Id}: invalid, {problems}").ConfigureAwait(false);
                continue;
            }

            var attempts = new List<AttemptRecord>();
            records.Add(new TaskRecord(task.Id, set, null, oracle.Notes, attempts));
            for (int attempt = 1; attempt <= repeat; attempt++)
            {
                AgentAttempt run = await AttemptAsync(task, attempt, retry: false).ConfigureAwait(false);
                if (run.IsRetryable)
                {
                    await progress.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{task.Id} #{attempt}: retrying in {retryDelay.TotalSeconds} s")).ConfigureAwait(false);
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    await AttemptAsync(task, attempt, retry: true).ConfigureAwait(false);
                }
            }

            async Task<AgentAttempt> AttemptAsync(EvalTask task, int attempt, bool retry)
            {
                int audited = auditedWrites();
                AgentAttempt run = await runner.RunAsync(task, attempt, retry, cancellationToken).ConfigureAwait(false);
                bool isolated = !run.Trace.Calls.Any(c => c.Committed) || audited < 0 || auditedWrites() != audited;
                if (!isolated)
                {
                    run = run with { Status = AttemptStatus.Invalid, Reason = NotIsolated };
                }

                Verdict? verdict = null;
                if (run.Status == AttemptStatus.Answered)
                {
                    try
                    {
                        IReadOnlyDictionary<string, AfterValue> after = await AfterRunner.RunAsync(task, client, null, cancellationToken).ConfigureAwait(false);
                        verdict = Grader.Grade(task, run.Answer, oracle.Values, after);
                    }
                    catch (Exception ex)
                    {
                        // The attempt is paid for, so it is reported before the run stops (Ctrl+C, eplanmcp gone).
                        await RecordAsync(attempt, run with { Status = AttemptStatus.Invalid, Reason = "its after calls failed: " + ex.Message }, null, retry).ConfigureAwait(false);
                        throw;
                    }
                }

                await RecordAsync(attempt, run, verdict, retry).ConfigureAwait(false);

                // Every later attempt would write unprotected too, and the fixture may have changed: the run stops.
                return isolated ? run : throw new InvalidOperationException(NotIsolated);
            }

            async Task RecordAsync(int attempt, AgentAttempt run, Verdict? verdict, bool retry)
            {
                AttemptRecord record = AttemptRecord.From(attempt, run, verdict, retry);
                attempts.Add(record);
                await progress.WriteLineAsync(Describe(task.Id, record)).ConfigureAwait(false);
            }
        }
    }

    private static string Describe(string id, AttemptRecord record) => string.Create(
        CultureInfo.InvariantCulture,
        $"{id} #{record.Attempt}{(record.Retry ? " (retry)" : string.Empty)}: {record.Outcome}{(record.Reason is { } reason ? ", " + reason : string.Empty)} ({record.ToolCalls.Values.Sum()} calls, {record.Seconds} s, {record.CostUsd:0.00} USD)");
}
