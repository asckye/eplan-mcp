namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Makes new projects, implemented by the EPLAN adapter layer (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.8, 4.9).
/// Main thread only; the operations have checked that the project's folder is empty.
/// </summary>
public interface IProjectFactory
{
    /// <summary>
    /// <c>ProjectManager.CreateProject(linkFile, basicProject)</c> (api-notes A31) with the page count and <c>PROJ_GUID</c> read
    /// in the same call: the project is never closed through the object CreateProject returns, nor opened again (A34).
    /// Throws EPLAN_OPERATION_FAILED when EPLAN refuses.
    /// </summary>
    public (int Pages, string? ProjectGuid) Create(string linkFile, string basicProject);
}
