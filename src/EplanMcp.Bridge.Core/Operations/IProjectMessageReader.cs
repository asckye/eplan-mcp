namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads a project's check messages, implemented by the EPLAN adapter layer. Called on the main thread only, with a
/// path that <see cref="ProjectResolver"/> chose in the same call. Reading only: it never runs a check, which
/// rewrites the messages (api-notes A11).
/// </summary>
public interface IProjectMessageReader
{
    /// <summary>Every check message of the open project at <paramref name="projectPath"/>, in EPLAN's order.</summary>
    public IReadOnlyList<ProjectMessageRecord> ReadMessages(string projectPath);
}
