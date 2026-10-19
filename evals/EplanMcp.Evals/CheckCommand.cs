using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals;

/// <summary><c>check</c>: validates every task file, without EPLAN or a model (agent evals design 4.4).</summary>
internal static class CheckCommand
{
    /// <returns>0 when every file is valid, 1 otherwise.</returns>
    public static int Run(string directory, TextWriter output)
    {
        if (!Directory.Exists(directory))
        {
            output.WriteLine($"There is no task directory {directory}.");
            return 1;
        }

        // A task saved as .yml would otherwise be skipped without a word.
        string[] misnamed = [.. Directory.EnumerateFiles(directory, "*.yml").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];
        foreach (string file in misnamed)
        {
            output.WriteLine($"{file}: task files end in .yaml; rename it");
        }

        IReadOnlyList<TaskFile> files = TaskLoader.LoadDirectory(directory);
        foreach (TaskFile file in files)
        {
            foreach (string problem in file.Problems)
            {
                output.WriteLine($"{file.Name}.yaml {problem}");
            }
        }

        EvalTask[] tasks = [.. files.Select(f => f.Task).OfType<EvalTask>()];
        int invalid = files.Count - tasks.Length + misnamed.Length;
        output.WriteLine(
            $"{tasks.Length} valid tasks (dev {tasks.Count(t => t.Set == TaskSet.Dev)}, holdout {tasks.Count(t => t.Set == TaskSet.Holdout)}), {invalid} invalid files in {directory}.");
        return invalid == 0 ? 0 : 1;
    }
}
