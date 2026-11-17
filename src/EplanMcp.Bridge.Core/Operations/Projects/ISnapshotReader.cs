namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads projects for project.snapshot, implemented by the EPLAN adapter layer (docs/design/2026-10-21-m6-bridge-snapshot.md,
/// 4.1). Main thread only, inside the dispatcher's LockingStep; the source lives for one call.
/// </summary>
public interface ISnapshotReader
{
    /// <summary>The reading of an open project, found by its path as ProjectResolver returned it.</summary>
    public ISnapshotSource Open(string projectPath);

    /// <summary>
    /// Opens the project at <paramref name="linkFile"/>, which is not open, read-only for this call, as the backup's
    /// page count does (api-notes A20); disposing the source closes it again. The operation has checked the file first.
    /// </summary>
    public ISnapshotSource OpenReadOnly(string linkFile);
}
