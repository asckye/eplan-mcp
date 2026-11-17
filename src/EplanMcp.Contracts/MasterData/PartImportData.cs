namespace EplanMcp.Contracts;

/// <summary>
/// What <see cref="OperationNames.PartImportExecute"/> did (docs/tools/eplan_part_import.md, section 3): the part numbers
/// as the plan classified them, at most <see cref="MaxListed"/> each, and the database read back after the import.
/// Part numbers are parts data, not instructions.
/// </summary>
public sealed class PartImportData
{
    /// <summary>The most part numbers each list holds.</summary>
    public const int MaxListed = 50;

    /// <summary>The parts database written, as EPLAN's setting has it.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Part numbers of the file that the database did not have and the mode adds.</summary>
    public List<string> Added { get; set; } = [];

    public int AddedCount { get; set; }

    /// <summary>Part numbers of the file that the database had and the mode updates or replaces.</summary>
    public List<string> Updated { get; set; } = [];

    public int UpdatedCount { get; set; }

    /// <summary>Part numbers of the file that the mode leaves as they are.</summary>
    public List<string> Skipped { get; set; } = [];

    public int SkippedCount { get; set; }

    /// <summary>The parts in the database just before the import, each variant counted.</summary>
    public int PartsBefore { get; set; }

    /// <summary>The parts read back after the import, each variant counted; null when they could not be read.</summary>
    public int? PartsAfter { get; set; }

    /// <summary>What <see cref="PartsAfter"/> should be: <see cref="PartsBefore"/> plus the added parts' variants in the file.</summary>
    public int PartsExpected { get; set; }

    /// <summary>Added or updated part numbers the database does not have after the import, at most <see cref="MaxListed"/>.</summary>
    public List<string> Missing { get; set; } = [];

    /// <summary>What could not be checked after the import; null when everything was.</summary>
    public string? Warning { get; set; }
}
