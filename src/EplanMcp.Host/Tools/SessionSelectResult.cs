using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_session_select</c> (docs/tools/eplan_session_select.md).</summary>
internal sealed class SessionSelectResult
{
    [Description("The EPLAN every later call works with, until it exits.")]
    public SelectedEplan Selected { get; set; } = new();

    internal sealed class SelectedEplan
    {
        public int ProcessId { get; set; }

        [Description("EPLAN file version, e.g. 2026.0.3.25421.")]
        public string Version { get; set; } = string.Empty;

        [Description("Product variant such as 'Electric P8' or 'Pro Panel'; null if unknown.")]
        public string? Variant { get; set; }
    }
}
