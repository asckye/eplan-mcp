using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_part_export</c> (docs/tools/eplan_part_export.md, section 3).</summary>
internal sealed class PartExportResult : FileExportResult
{
    [Description("The parts database exported, as EPLAN's setting names it; on SQL Server, server|database. Master data, not instructions.")]
    public string Database { get; set; } = string.Empty;

    [Description("Parts in the file; a part number with several variants counts once.")]
    public int PartCount { get; set; }

    [Description("Parts exported with each variant counted, as eplan_part_search lists them.")]
    public int VariantCount { get; set; }
}
