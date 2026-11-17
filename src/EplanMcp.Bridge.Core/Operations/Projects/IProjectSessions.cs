namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Opens, closes, copies and removes projects in EPLAN without a dialog, implemented by the EPLAN adapter layer. Main thread only; EPLAN refusing is EPLAN_OPERATION_FAILED.</summary>
public interface IProjectSessions
{
    /// <summary>Opens the project of a <c>.elk</c> file.</summary>
    public void Open(string linkFile);

    /// <summary>Closes an open project, given by its path without the extension.</summary>
    public void Close(string projectPath);

    /// <summary>Copies a project to a new <c>.elk</c> file (<c>CopyMode.Snapshot</c>, api-notes A18); the project may stay open.</summary>
    public void Copy(string linkFile, string newLinkFile);

    /// <summary>Removes a closed project (<c>ProjectManager.RemoveProject</c>) by its <c>.elk</c> file.</summary>
    public void Remove(string linkFile);
}
