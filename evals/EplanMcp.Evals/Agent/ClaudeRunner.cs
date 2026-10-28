using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Agent;

/// <summary>Where claude is and how it is set up for every attempt; <see cref="McpConfigFile"/> holds the MCP configuration.</summary>
internal sealed record ClaudeSetup(string Claude, string Model, string McpConfigFile);

/// <summary>
/// Runs one attempt of a task with <c>claude -p</c> and enforces its budget (agent evals design 4.2, steps 3 and 4).
/// The raw stream goes to <c>transcript.jsonl</c> and stderr to <c>stderr.txt</c> in the output directory.
/// </summary>
internal static class ClaudeRunner
{
    public const string TranscriptFile = "transcript.jsonl";
    public const string StderrFile = "stderr.txt";

    // claude has written its result once stdout ends; this only covers a process, or a child holding its stderr, that
    // lingers after that.
    private static readonly TimeSpan _exitGrace = TimeSpan.FromSeconds(30);

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// <paramref name="workDirectory"/> must be an empty directory outside the repository, so that no CLAUDE.md is found
    /// above it; the files go to <paramref name="outputDirectory"/>. The time limit, the exit grace and the elapsed time
    /// all run on <paramref name="clock"/>.
    /// </summary>
    public static async Task<AgentAttempt> RunAsync(ClaudeSetup setup, EvalTask task, string workDirectory, string outputDirectory, TimeProvider clock, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(setup.Claude)
        {
            WorkingDirectory = workDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = _utf8,
            StandardOutputEncoding = _utf8,
            StandardErrorEncoding = _utf8,
        };
        foreach (string argument in ClaudeCommand.Arguments(task, setup.Model, setup.McpConfigFile))
        {
            start.ArgumentList.Add(argument);
        }

        long started = clock.GetTimestamp();
        using Process process = Start(start);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var trace = new AgentTrace();
        AgentStop? stop = null;

        // A silent claude (a long model turn) blocks the read, so the time limit kills the process instead of
        // cancelling the read; the read then ends with the closed pipe.
        using var timeLimit = new CancellationTokenSource(task.Budget.MaxDuration, clock);
        using (CancellationTokenRegistration onTime = timeLimit.Token.Register(() => Kill(process)))
        using (CancellationTokenRegistration onCancel = cancellationToken.Register(() => Kill(process)))
        {
            try
            {
                await process.StandardInput.WriteAsync(task.Prompt).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // claude exited before reading its prompt; stderr and the missing result tell why
            }

            var transcript = new StreamWriter(Path.Combine(outputDirectory, TranscriptFile), append: false, _utf8);
            await using (transcript.ConfigureAwait(false))
            {
                while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                {
                    await transcript.WriteLineAsync(line).ConfigureAwait(false);
                    trace.Read(line);
                    stop = StopFor(trace, task.Budget);
                    if (stop is not null)
                    {
                        Kill(process);
                        break;
                    }
                }
            }
        }

        if (stop is null && trace.Result is null && timeLimit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stop = new AgentStop(AttemptStatus.Failed, $"over the time limit of {task.Budget.MaxDuration.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture)} minutes");
        }

        await ExitAsync(process, clock).ConfigureAwait(false);
        TimeSpan elapsed = clock.GetElapsedTime(started);
        string errors;
        try
        {
            errors = await stderr.WaitAsync(_exitGrace, clock, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            errors = "(stderr stayed open after claude exited)";
        }

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, StderrFile), errors, _utf8, CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentAttempt.From(trace, stop, process.ExitCode, errors, elapsed);
    }

    /// <summary>Whether the runner must stop claude now, given what the stream showed so far.</summary>
    internal static AgentStop? StopFor(AgentTrace trace, Budget budget)
    {
        if (trace.IsolationProblem() is { } problem)
        {
            return new AgentStop(AttemptStatus.Invalid, "not isolated: " + problem);
        }

        return trace.Calls.Count > budget.MaxToolCalls ? new AgentStop(AttemptStatus.Failed, $"more than {budget.MaxToolCalls} tool calls") : null;
    }

    private static Process Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not start claude at {start.FileName}: {ex.Message}", ex);
        }
    }

    private static async Task ExitAsync(Process process, TimeProvider clock)
    {
        using var grace = new CancellationTokenSource(_exitGrace, clock);
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Ends claude together with the eplanmcp it started; a process that already exited is fine.</summary>
    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // already exited
        }
    }
}
