using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_import_pxf</c> (docs/tools/eplan_import_pxf.md, section 3).</summary>
internal sealed class ImportPxfResult
{
    [Description("committed, or not_started for a dry run: whether the project was made.")]
    public string Execution { get; set; } = string.Empty;

    [Description("True when only the plan was returned.")]
    public bool DryRun { get; set; }

    [Description("The file's path below the imports folder.")]
    public string File { get; set; } = string.Empty;

    [Description("The file's SHA-256, lowercase hex, as the audit names it.")]
    public string Sha256 { get; set; } = string.Empty;

    [Description("The file's size in bytes.")]
    public long Bytes { get; set; }

    [Description("The new project, in the scratch folder; for a dry run, where it would be.")]
    public ProjectEntry Project { get; set; } = new();

    [Description("How long the import took, in seconds; null for a dry run or when 'recovered'.")]
    public int? Seconds { get; set; }

    [Description("True when the response was lost and EPLAN reported the import afterwards.")]
    public bool Recovered { get; set; }

    [Description("What to do next; null for a dry run.")]
    public string? Hint { get; set; }

    internal sealed class ProjectEntry
    {
        [Description("The new project's name.")]
        public string Name { get; set; } = string.Empty;

        [Description("The new project's .elk file, in the scratch folder.")]
        public string Path { get; set; } = string.Empty;

        [Description("Always false: the import does not open the project in EPLAN.")]
        public bool Opened { get; set; }
    }
}
