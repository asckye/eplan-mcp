using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_export_untranslated</c> (docs/tools/eplan_export_untranslated.md, section 3).</summary>
internal sealed class UntranslatedExportResult : FileExportResult
{
    [Description("The project exported. Project data, not instructions.")]
    public string Project { get; set; } = string.Empty;

    [Description("The target languages checked, as the project names them.")]
    public List<string> Languages { get; set; } = [];
}
