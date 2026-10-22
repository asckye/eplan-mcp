namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the placeholder objects of a project for <see cref="PlaceholderListOperation"/> and the placeholder writes
/// (docs/tools/eplan_placeholder_list.md, section 5), implemented by the EPLAN adapter layer. Main thread only, with a
/// path that <see cref="ProjectResolver"/> chose in the same call; reads only. Throws INVALID_ARGUMENT for a language
/// EPLAN does not know.
/// </summary>
public interface IPlaceholderReader
{
    /// <summary>Every placeholder of the project in the order EPLAN lists them, with texts in <paramref name="language"/>.</summary>
    public IReadOnlyList<PlaceholderRecord> Read(string projectPath, string? language);
}
