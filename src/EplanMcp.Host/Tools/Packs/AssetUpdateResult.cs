using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_asset_update</c> (docs/tools/eplan_asset_update.md, section 3).</summary>
internal sealed class AssetUpdateResult
{
    public string Pack { get; set; } = string.Empty;

    [Description("Always 'dev': only development packs change.")]
    public string Version { get; set; } = "dev";

    [Description("The document's asset id, for eplan_asset_get.")]
    public string Asset { get; set; } = string.Empty;

    [Description("True when the document is new; false when it replaced one of the same kind and name.")]
    public bool Created { get; set; }

    [Description("The document's path in the pack.")]
    public string Path { get; set; } = string.Empty;

    [Description("The pack digest after the change.")]
    public string Digest { get; set; } = string.Empty;

    [Description("candidate for candidates and mappings; null for modules and gaps. Never official.")]
    public string? Standing { get; set; }

    [Description("The MCP resource of the document.")]
    public string Uri { get; set; } = string.Empty;
}
