namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the placeholder objects of a project for <see cref="PlaceholderListOperation"/> and the placeholder writes
/// (docs/tools/eplan_placeholder_list.md, section 5), implemented by the EPLAN adapter layer. Main thread only, with a
/// path that <see cref="ProjectResolver"/> chose in the same call; reads only. Throws INVALID_ARGUMENT for a language
/// EPLAN does not know.
/// </summary>
public interface IPlaceholderReader
{
    /// <summary>
    /// Every placeholder of the project in the order EPLAN lists them, with texts in <paramref name="language"/>. The
    /// current values only when <paramref name="currentValues"/>: EPLAN counts reading them as write access and refuses
    /// it for a placeholder that is not locked (2026-10-22, api-notes A33), which a read never does; a refused one has
    /// <see cref="PlaceholderRecord.CurrentValuesRefused"/> set instead of failing the read.
    /// </summary>
    public IReadOnlyList<PlaceholderRecord> Read(string projectPath, string? language, bool currentValues);
}
