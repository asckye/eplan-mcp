using System.ComponentModel;

namespace EplanMcp.Host.Tools;

internal sealed record PluginInstallResult(
    [property: Description("Installed exact version and outcome, or null when source data could not be read.")] PackImportResult.InstalledEntry? Installed,
    [property: Description("Missing or unreadable source evidence; no installation occurred when installed is null.")] IReadOnlyList<string> Warnings);
