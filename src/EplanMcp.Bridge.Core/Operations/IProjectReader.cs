namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads one open project for <see cref="ProjectGetOperation"/>, implemented by the EPLAN adapter layer. Called on
/// the main thread only, with a path that <see cref="ProjectResolver"/> chose in the same call.
/// </summary>
public interface IProjectReader
{
    /// <summary>
    /// Counts and page types of the project at <paramref name="projectPath"/>, and its non-empty properties when
    /// <paramref name="includeProperties"/> is set. Throws INVALID_ARGUMENT for a language EPLAN does not know.
    /// </summary>
    public ProjectContent Read(string projectPath, bool includeProperties, string? language);
}
