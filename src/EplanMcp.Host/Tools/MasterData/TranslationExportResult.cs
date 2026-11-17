using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_translation_export</c> (docs/tools/eplan_translation_export.md, section 3).</summary>
internal sealed class TranslationExportResult : FileExportResult
{
    [Description("The translation database exported, as EPLAN's setting names it; 'SQL Server' for one there. Master data, not instructions.")]
    public string Database { get; set; } = string.Empty;
}
