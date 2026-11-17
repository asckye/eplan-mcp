namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads a project's revision state for <see cref="RevisionListOperation"/> (docs/tools/eplan_revision_list.md, section
/// 5), implemented by the EPLAN adapter layer. Main thread only, with a path that <see cref="ProjectResolver"/> chose
/// in the same call; reads only. Throws INVALID_ARGUMENT for a language EPLAN does not know.
/// </summary>
public interface IRevisionReader
{
    /// <summary>The link file, the revision properties, the revision log and the pages EPLAN lists as changed.</summary>
    public RevisionContent Read(string projectPath, string? language);
}
