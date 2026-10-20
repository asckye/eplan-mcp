using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_session_status</c> (docs/tools/eplan_session_status.md).</summary>
internal sealed class SessionStatusResult
{
    [Description("True when the EPLAN in use (the only one running, or the one chosen with eplan_session_select) answered with a compatible add-in.")]
    public bool Connected { get; set; }

    [Description("This EplanMcp server.")]
    public HostSummary Host { get; set; } = new();

    [Description("The connected EPLAN; null when not connected.")]
    public EplanSummary? Eplan { get; set; }

    [Description("Every EPLAN process found with the EplanMcp add-in loaded.")]
    public List<InstanceSummary> Instances { get; set; } = [];

    [Description("What to do next when not connected, or what to know about a connected EPLAN that EplanMcp has not been tested with; null otherwise.")]
    public string? Hint { get; set; }

    internal sealed class HostSummary
    {
        public string Version { get; set; } = string.Empty;

        public int ProtocolVersion { get; set; }
    }

    internal sealed class EplanSummary
    {
        public int ProcessId { get; set; }

        [Description("EPLAN file version, e.g. 2026.0.3.25421.")]
        public string Version { get; set; } = string.Empty;

        [Description("Product variant such as 'Electric P8' or 'Pro Panel'; null if unknown.")]
        public string? Variant { get; set; }

        public string BridgeVersion { get; set; } = string.Empty;

        public int ProtocolVersion { get; set; }

        [Description("Whether the EPLAN API extension licence is available; null if unknown.")]
        public bool? ApiExtensionLicence { get; set; }
    }

    internal sealed class InstanceSummary
    {
        [Description("Pass it as 'process_id' to eplan_session_select to work with this EPLAN.")]
        public int ProcessId { get; set; }

        [Description("True for the EPLAN chosen with eplan_session_select.")]
        public bool Selected { get; set; }
    }
}
