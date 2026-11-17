using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PluginEnableTools
{
    public const string Tool = "eplan_pack_enable";

    [McpServerTool(Name = Tool, Title = "Enable or disable a plugin globally", ReadOnly = false, Destructive = false,
        Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PluginEnableResult))]
    [Description("Enables an exact installed plugin version globally, or disables it without uninstalling. Requires one approval in EPLAN; repeating the same request changes nothing and asks nothing. New IDs append in enable order; another version of an enabled ID replaces it in place. Project bindings stay unchanged. Example: {\"plugin\":\"iec-base\",\"version\":\"1.0.0\",\"enabled\":true}.")]
    public static async Task<CallToolResult> SetAsync(PluginEnableService service,
        [Description("Exact installed plugin ID; no path.")][MinLength(1), MaxLength(100)] string plugin,
        [Description("Exact installed SemVer version.")][MinLength(1), MaxLength(128)] string version,
        [Description("true to enable globally; false to disable this exact version.")] bool enabled,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(plugin) || plugin.Length > 100 || plugin.Any(char.IsControl) || !PackManifestValidator.IsId(plugin))
        { errors.Add("plugin must be an exact plugin ID, without paths or control characters."); }
        if (string.IsNullOrEmpty(version) || version.Length > 128 || version.Any(char.IsControl) || !SemanticVersion.TryParse(version, out _))
        { errors.Add("version must be an exact SemVer version, such as 1.0.0."); }
        if (errors.Count > 0)
        { return ToolResults.InvalidArguments(errors); }
        try
        {
            PluginEnableResult result = await service.SetAsync(plugin, version, enabled, progress, cancellationToken).ConfigureAwait(false);
            return ToolResults.Success(result.Changed ? "Global plugin enablement changed." : "Global plugin enablement was not changed.", result);
        }
        catch (BridgeCallException ex)
        { return ToolResults.Error(ex.Error); }
    }
}
