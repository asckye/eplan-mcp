using System.Globalization;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Reporting;
using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Server;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Run;

/// <summary>One attempt of a task by the model; <see cref="ClaudeAttempts"/> runs <c>claude -p</c>.</summary>
internal interface IAttemptRunner
{
    public Task<AgentAttempt> RunAsync(EvalTask task, int attempt, CancellationToken cancellationToken);
}

/// <summary>Runs the tasks one after another (agent evals design 4.2, steps 2 to 5).</summary>
internal static class EvalRun
{
    /// <summary>
    /// Appends one record per task to <paramref name="records"/> as soon as the task starts, so that a cancelled run
    /// still reports what it finished. A task whose oracle calls fail gets no attempt.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<EvalTask> tasks,
        int repeat,
        IToolClient client,
        IReadOnlySet<string> tools,
        IAttemptRunner runner,
        TextWriter progress,
        List<TaskRecord> records,
        CancellationToken cancellationToken)
    {
        foreach (EvalTask task in tasks)
        {
            string set = task.Set == TaskSet.Dev ? "dev" : "holdout";
            OracleValues oracle = await OracleRunner.RunAsync(task, client, tools, cancellationToken).ConfigureAwait(false);
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
                AgentAttempt run = await runner.RunAsync(task, attempt, cancellationToken).ConfigureAwait(false);
                Verdict? verdict = run.Status == AttemptStatus.Answered ? Grader.Grade(task, run.Answer, oracle.Values) : null;
                AttemptRecord record = AttemptRecord.From(attempt, run, verdict);
                attempts.Add(record);
                await progress.WriteLineAsync(Describe(task.Id, record)).ConfigureAwait(false);
            }
        }
    }

    private static string Describe(string id, AttemptRecord record) => string.Create(
        CultureInfo.InvariantCulture,
        $"{id} #{record.Attempt}: {record.Outcome}{(record.Reason is { } reason ? ", " + reason : string.Empty)} ({record.ToolCalls.Values.Sum()} calls, {record.Seconds} s, {record.CostUsd:0.00} USD)");
}
