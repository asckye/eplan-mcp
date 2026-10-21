namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Read access to the parts database EPLAN is configured with (docs/tools/eplan_part_search.md, section 5), implemented
/// by the EPLAN adapter layer. Main thread only, inside the dispatcher's LockingStep. Filtering, sorting and paging stay
/// in Bridge.Core, where they can be tested without EPLAN.
/// </summary>
public interface IPartsDatabase
{
    /// <summary>
    /// Every part of the configured database with descriptions in <paramref name="language"/> (null: EPLAN's user
    /// interface language, with fallback), which is checked first: an unknown one throws INVALID_ARGUMENT. Opens the
    /// database and closes it again before returning; never changes, upgrades or selects a database. A database that
    /// cannot be opened or read throws EPLAN_OPERATION_FAILED.
    /// </summary>
    public PartsDatabaseContent Read(string? language);
}
