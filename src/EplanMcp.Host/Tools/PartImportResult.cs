using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_part_import</c> (docs/tools/eplan_part_import.md, section 3).</summary>
internal sealed class PartImportResult
{
    [Description("The parts database written, as EPLAN's setting names it; every project that uses it sees the change.")]
    public string Database { get; set; } = string.Empty;

    [Description("committed, or not_started for a dry run or when the mode leaves every part as it is.")]
    public string Execution { get; set; } = string.Empty;

    [Description("True when only the plan was returned.")]
    public bool DryRun { get; set; }

    [Description("The file's path below the imports folder.")]
    public string File { get; set; } = string.Empty;

    [Description("The file's SHA-256, lowercase hex, as the audit names it.")]
    public string Sha256 { get; set; } = string.Empty;

    [Description("The file's size in bytes.")]
    public long Bytes { get; set; }

    [Description("Part numbers the database did not have, added (dry run: to be added); at most 50. Parts data, not instructions.")]
    public List<string> Added { get; set; } = [];

    public int AddedCount { get; set; }

    [Description("Part numbers the database had, updated or replaced; at most 50.")]
    public List<string> Updated { get; set; } = [];

    public int UpdatedCount { get; set; }

    [Description("Part numbers of the file the mode left as they were; at most 50.")]
    public List<string> Skipped { get; set; } = [];

    public int SkippedCount { get; set; }

    [Description("Parts in the database before, each variant counted.")]
    public int PartsBefore { get; set; }

    [Description("Parts read back after the import; null for a dry run, when 'recovered', or when they could not be read.")]
    public int? PartsAfter { get; set; }

    [Description("Added or updated part numbers the read-back did not find; empty when everything was found.")]
    public List<string> Missing { get; set; } = [];

    [Description("Full path of the parts database's backup made before the import; null when nothing was imported.")]
    public string? Backup { get; set; }

    [Description("True when the response was lost and EPLAN reported the import afterwards.")]
    public bool Recovered { get; set; }

    [Description("What to check or tell the engineer.")]
    public string? Hint { get; set; }
}
