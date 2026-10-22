using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

/// <summary>Output of eplan_import_list (tool spec 3); business counts are not imported row counts.</summary>
internal sealed class ImportListResult
{
    public string Project { get; set; } = string.Empty;
    [AllowedValues("not_started", "committed")]
    public string Execution { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    [AllowedValues("devices", "parts")]
    public string Kind { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    [Range(1, long.MaxValue)]
    public long Bytes { get; set; }
    [RegularExpression("^[a-f0-9]{64}$")]
    public string Sha256 { get; set; } = string.Empty;
    [AllowedValues("XDLXmlExporter", "XDLCsvImporterExporter", "XPalXmlExporter")]
    public string Converter { get; set; } = string.Empty;
    public ListImportCounts Before { get; set; } = new();
    [Description("Read-back business counts; null for dry run or recovered outcome. Counts do not establish row-level effects.")]
    public ListImportCounts? After { get; set; }
    public string? Backup { get; set; }
    [Description("Always null: undo after commit has not been verified.")]
    public string? Undo { get; set; }
    public bool Recovered { get; set; }
    [MaxLength(20)]
    public List<MessageListResult.MessageEntry> EplanMessages { get; set; } = [];
    public bool Truncated { get; set; }
    public string? Hint { get; set; }
}
