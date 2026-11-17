namespace EplanMcp.Contracts;

/// <summary>
/// What <see cref="OperationNames.PartSyncExecute"/> did (docs/tools/eplan_part_sync.md, section 3): the part numbers as
/// the plan classified them, at most <see cref="PartImportData.MaxListed"/> each, and the parts database and the project
/// read back after the synchronization. Part numbers are parts data, not instructions.
/// </summary>
public sealed class PartSyncData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The parts database written, as EPLAN's setting has it.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Part numbers stored in the project that the database did not have, which the mode adds.</summary>
    public List<string> Added { get; set; } = [];

    public int AddedCount { get; set; }

    /// <summary>Part numbers stored in the project that the database had, which the mode overwrites with the project's data.</summary>
    public List<string> Overwritten { get; set; } = [];

    public int OverwrittenCount { get; set; }

    /// <summary>Part numbers the project referenced without storing them, which EPLAN stores in the project (S24 r15).</summary>
    public List<string> StoredInProject { get; set; } = [];

    public int StoredInProjectCount { get; set; }

    /// <summary>The database's parts before and after, each variant counted; after is null when they could not be read.</summary>
    public int PartsBefore { get; set; }

    public int? PartsAfter { get; set; }

    /// <summary>What <see cref="PartsAfter"/> should be: <see cref="PartsBefore"/> plus the variants the project stores of the added parts.</summary>
    public int PartsExpected { get; set; }

    /// <summary>The parts stored in the project before and after, each variant counted; after is null when unread.</summary>
    public int ProjectPartsBefore { get; set; }

    public int? ProjectPartsAfter { get; set; }

    /// <summary>Added or overwritten part numbers the database does not have after it, at most 50.</summary>
    public List<string> Missing { get; set; } = [];

    /// <summary>Part numbers of <see cref="StoredInProject"/> the project does not store after it, at most 50.</summary>
    public List<string> NotStored { get; set; } = [];

    /// <summary>What could not be checked after it, or what to know about the undo step; null when nothing.</summary>
    public string? Warning { get; set; }
}
