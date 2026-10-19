using EplanMcp.Evals.Run;

namespace EplanMcp.Evals;

/// <summary>
/// <c>EplanMcp.Evals check</c> validates the task files; <c>run</c> runs them against EPLAN and the model (agent evals
/// design 4.4). Ctrl+C stops a run after writing the report of what finished.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case ["check"]:
                return CheckCommand.Run(DefaultTasks(), Console.Out);
            case ["check", "--tasks", string directory]:
                return CheckCommand.Run(directory, Console.Out);
            case ["run", .. var rest]:
                if (RunOptions.Parse(rest, out string? error) is not { } options)
                {
                    Console.Error.WriteLine("run: " + error);
                    return Usage();
                }

                using (var cancel = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (_, e) =>
                    {
                        // The first Ctrl+C stops the run and still writes the report; a second one ends the process.
                        if (!cancel.IsCancellationRequested)
                        {
                            e.Cancel = true;
                            cancel.Cancel();
                        }
                    };
                    try
                    {
                        return await RunCommand.RunAsync(options, Console.Out, cancel.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                    {
                        Console.Error.WriteLine("Cancelled before the run started.");
                        return 130;
                    }
                }

            default:
                return Usage();
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: EplanMcp.Evals check [--tasks <dir>] | " + RunOptions.Usage);
        return 2;
    }

    /// <summary><c>evals/tasks</c> of the repository the command runs in, found from the working directory.</summary>
    private static string DefaultTasks() =>
        Path.Combine(ToolLocator.RepositoryRoot(Environment.CurrentDirectory) ?? string.Empty, "evals", "tasks");
}
