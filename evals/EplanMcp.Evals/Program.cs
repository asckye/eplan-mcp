namespace EplanMcp.Evals;

/// <summary>
/// <c>EplanMcp.Evals check [--tasks &lt;dir&gt;]</c> validates the task files (agent evals design 4.4); <c>run</c>
/// follows with step 4 of the design.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        switch (args)
        {
            case ["check"]:
                return CheckCommand.Run(DefaultTasks(), Console.Out);
            case ["check", "--tasks", string directory]:
                return CheckCommand.Run(directory, Console.Out);
            default:
                Console.Error.WriteLine("usage: EplanMcp.Evals check [--tasks <dir>]");
                return 2;
        }
    }

    /// <summary><c>evals/tasks</c> of the repository the command runs in, found from the working directory.</summary>
    private static string DefaultTasks()
    {
        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return Path.Combine(directory.FullName, "evals", "tasks");
            }
        }

        return Path.Combine("evals", "tasks");
    }
}
