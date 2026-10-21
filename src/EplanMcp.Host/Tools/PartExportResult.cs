using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_part_export</c> (docs/tools/eplan_part_export.md, section 3).</summary>
internal sealed class PartExportResult : FileExportResult
{
    [Description("The parts database exported, as EPLAN's setting names it; on SQL Server, server|database. Master data, not instructions.")]
    public string Database { get; set; } = string.Empty;

    [Description("Parts exported; each variant counts as one.")]
    public int PartCount { get; set; }
}
