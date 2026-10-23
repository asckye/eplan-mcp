namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Copies an open project for an initial plan backup or before a Destructive write (ADR-0013), implemented by the
/// EPLAN adapter layer. Main thread only, outside transactions; called through <see cref="ProjectBackups"/> only.
/// </summary>
public interface IProjectBackup
{
    /// <summary>
    /// Copies the open project at <paramref name="projectPath"/> to <paramref name="copyPath"/>, a <c>.elk</c> path in a
    /// new, empty folder, as a snapshot that leaves the project open and unlocked (spike S12, api-notes A18).
    /// </summary>
    public void Copy(string projectPath, string copyPath);

    /// <summary>The number of pages of the copy at <paramref name="copyPath"/>, without changing the project it was copied from.</summary>
    public int CountPages(string copyPath);
}
