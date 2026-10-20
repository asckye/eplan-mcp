namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Runs EPLAN's check, implemented by the EPLAN adapter layer (docs/tools/eplan_check_run.md, section 5). Page names are
/// exact, as the project has them; null pages stand for the whole project. The messages are read with
/// <see cref="IProjectMessageReader"/>. Main thread only; <see cref="Run"/> only inside the write's transaction.
/// </summary>
public interface ICheckRunner
{
    /// <summary>
    /// The session identifier (<c>ToStringIdentifier</c>, valid in this EPLAN session only, A3) of each given page, or
    /// of every page of the project.
    /// </summary>
    public IReadOnlyList<string> PageIds(string projectPath, IReadOnlyList<string>? pages);

    /// <summary>
    /// Checks the pages, or the whole project, with <paramref name="scheme"/>; an empty scheme is the one EPLAN uses
    /// now. The check replaces the project's check messages (api-notes A11, A16). A scheme EPLAN does not know is
    /// INVALID_ARGUMENT, another failure of EPLAN's EPLAN_OPERATION_FAILED.
    /// </summary>
    public void Run(string projectPath, IReadOnlyList<string>? pages, string scheme);
}
