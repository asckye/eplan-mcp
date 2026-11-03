using System.ComponentModel;
using EplanMcp.Contracts;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PluginInstallTools
{
    public const string Tool = "eplan_pack_install";

    [McpServerTool(Name = Tool, Title = "Install a library plugin", ReadOnly = false, Destructive = false,
        Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PluginInstallResult))]
    [Description("Installs an exact plugin version from eplan_pack_library_list; arguments are names, never paths. Builtin needs no approval; other sources ask in EPLAN's pack-install dialog. Denial leaves nothing installed. Does not enable plugins or change project bindings. Same version and digest is a no-op. Example: {\"library\":\"builtin\",\"plugin\":\"iec-base\",\"version\":\"1.0.0\"}.")]
    public static async Task<CallToolResult> InstallAsync(PluginLibrarySource libraries, PackInstaller installer,
        [Description("Exact library name from eplan_pack_library_list.")] string library,
        [Description("Exact plugin ID from that library.")] string plugin,
        [Description("Exact SemVer from that library.")] string version,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(library) || library.Length > 100 || library is "." or ".." || library.IndexOfAny(['/', '\\', ':']) >= 0 || library.Any(char.IsControl))
        { violations.Add("library must be an exact library name, never a path."); }
        if (string.IsNullOrEmpty(plugin) || !PackManifestValidator.IsId(plugin) || plugin.Any(char.IsControl))
        { violations.Add("plugin must be an exact plugin ID, never a path."); }
        if (string.IsNullOrEmpty(version) || version.Length > 128 || version.Any(char.IsControl) || !SemanticVersion.TryParse(version, out _))
        { violations.Add("version must be an exact SemVer, never a path."); }
        if (violations.Count != 0)
        { return ToolResults.InvalidArguments(violations); }

        var (sources, warnings) = await libraries.ReadAsync(cancellationToken).ConfigureAwait(false);
        PluginLibraryReadResult[] matches = [.. sources.Where(s => s.Name == library && s.Readable)];
        if (matches.Length > 1)
        { return ToolResults.Error(new() { Reason = ErrorReason.AmbiguousName, Message = "Multiple readable libraries have this name; ask the engineer to rename one, then list libraries again." }); }
        if (matches.Length == 0 && sources.Any(s => s.Name == library))
        { return ToolResults.Success("Library data could not be read; nothing was installed.", new PluginInstallResult(null, [.. warnings, .. sources.Where(s => s.Name == library).SelectMany(s => s.Warnings)])); }
        PluginLibraryReadResult? source = matches.SingleOrDefault();
        if (source is null && warnings.Count != 0)
        { return ToolResults.Success("Library discovery is incomplete; nothing was installed.", new PluginInstallResult(null, warnings)); }
        PluginLibraryEntry? entry = source?.Index?.Plugins.SingleOrDefault(p => p.Id == plugin && p.Version == version);
        if (source is null || entry is null)
        { return ToolResults.Error(new() { Reason = ErrorReason.ResourceNotFound, Message = "No such library entry; call eplan_pack_library_list and select exact names and version." }); }
        var (installed, digest, error, gaps) = await installer.InstallLibraryAsync(source, entry, progress, cancellationToken).ConfigureAwait(false);
        if (gaps.Count != 0)
        { return ToolResults.Success("Library archive could not be read; nothing was installed.", new PluginInstallResult(null, [.. warnings, .. gaps])); }
        return installed is null ? ToolResults.Error(error!) : ToolResults.Success(
            installed.Outcome == PackInstallOutcome.Installed ? "Plugin installed; enable it separately to use its defaults." : "The same plugin version and digest is already installed; nothing changed.",
            new PluginInstallResult(new() { Id = plugin, Version = version, Digest = digest!, Outcome = PackTools.Snake(installed.Outcome) }, warnings));
    }
}
