namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Opens and closes projects in EPLAN without a dialog, implemented by the EPLAN adapter layer. Main thread only; EPLAN refusing is EPLAN_OPERATION_FAILED.</summary>
public interface IProjectSessions
{
    /// <summary>Opens the project of a <c>.elk</c> file.</summary>
    public void Open(string linkFile);

    /// <summary>Closes an open project, given by its path without the extension.</summary>
    public void Close(string projectPath);
}
