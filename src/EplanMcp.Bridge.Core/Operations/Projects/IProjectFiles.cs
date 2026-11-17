namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// What EPLAN tells about a project file without opening it (api-notes A31), implemented by the EPLAN adapter layer.
/// Main thread only.
/// </summary>
public interface IProjectFiles
{
    /// <summary>
    /// <c>ProjectManager.GetProjectDataCompatibility(linkFile)</c> as its enum name (<c>IsCompatible</c> for a
    /// 2026.0.3 project) and <c>GetProjectDatabaseVersion(linkFile)</c> as text (<c>18.0</c>). The file exists.
    /// </summary>
    public (string Status, string DatabaseVersion) Compatibility(string linkFile);
}
