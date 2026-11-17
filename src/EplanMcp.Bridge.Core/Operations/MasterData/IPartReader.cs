namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Read access to the parts a project uses, implemented by the EPLAN adapter layer. Called on the main thread only,
/// inside the dispatcher's LockingStep. It returns plain records; summing up, joining, filtering and paging stay in
/// Bridge.Core, where they can be tested without EPLAN.
/// </summary>
public interface IPartReader
{
    /// <summary>
    /// Every part reference of the open project at <paramref name="projectPath"/> and the project's own part data, with
    /// texts in <paramref name="language"/> (null: EPLAN's user interface language), which is checked first: an
    /// unknown one throws INVALID_ARGUMENT. Never reads the parts database.
    /// </summary>
    public ProjectParts ReadParts(string projectPath, string? language);
}
