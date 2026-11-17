using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_pack_build</c> (docs/tools/eplan_pack_build.md, section 3).</summary>
internal sealed class PackBuildResult
{
    public string Pack { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    [Description("The version's pack digest, which project locks record.")]
    public string Digest { get; set; } = string.Empty;

    [Description("installed, or already_installed when this version was built from the same content before.")]
    public string Outcome { get; set; } = string.Empty;

    public int Resources { get; set; }

    [Description("Where the version lies, relative to EplanMcp's data folder; it never changes.")]
    public string Location { get; set; } = string.Empty;

    [Description("The MCP resource of the version's manifest.")]
    public string Uri { get; set; } = string.Empty;
}
