using System.Globalization;
using EplanMcp.Evals.Agent;
using EplanMcp.Evals.Tasks;

namespace EplanMcp.Evals.Run;

/// <summary>
/// Runs each attempt in a new empty directory under <paramref name="workRoot"/>, outside the repository (design 4.2,
/// step 3), and keeps its transcript in <c>&lt;run&gt;/&lt;task&gt;/</c>, or <c>&lt;task&gt;-&lt;n&gt;/</c> when
/// tasks repeat; a retry adds <c>-retry</c>. The files that appear below <paramref name="exportRoot"/>, the export root
/// of the model's eplanmcp, during an attempt are its <see cref="AgentAttempt.Exports"/>.
/// </summary>
internal sealed class ClaudeAttempts(ClaudeSetup setup, string runDirectory, string workRoot, bool numbered, string exportRoot) : IAttemptRunner
{
    public async Task<AgentAttempt> RunAsync(EvalTask task, int attempt, bool retry, CancellationToken cancellationToken)
    {
        string name = (numbered ? string.Create(CultureInfo.InvariantCulture, $"{task.Id}-{attempt}") : task.Id) + (retry ? "-retry" : string.Empty);
        string output = Directory.CreateDirectory(Path.Combine(runDirectory, name)).FullName;
        string work = Directory.CreateDirectory(Path.Combine(workRoot, name)).FullName;
        try
        {
            IReadOnlyList<ExportedFile> before = ExportedFiles.List(exportRoot);
            AgentAttempt run = await ClaudeRunner.RunAsync(setup, task, work, output, TimeProvider.System, cancellationToken).ConfigureAwait(false);
            return run with { Exports = ExportedFiles.Since(before, ExportedFiles.List(exportRoot)) };
        }
        finally
        {
            // Only the runner's own scratch directory, which the model could not write to (no file tools).
            TryDelete(work);
        }
    }

    /// <summary>
    /// Best effort: an eplanmcp that is still exiting may hold the directory as its current one, and a leftover in the
    /// system temp directory must not end the run.
    /// </summary>
    public static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // left for the system's temp cleanup
        }
    }
}
