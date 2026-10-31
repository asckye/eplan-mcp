using System.Text.Json;

namespace EplanMcp.Host.Tools;

internal sealed record ActionCatalogItem(string Name, string Source, string? Type, string? Location, string? UiText,
    JsonElement[] Parameters, string Tier, string? ExcludedReason, string? WhitelistEntry, string? VerificationReference, string Reference);
internal sealed record ActionCatalogData(ActionCatalogItem[] Items, string? InstallationVersion, string[] Gaps)
{
    internal string Fingerprint => PageCursor.Fingerprint("action_catalog", JsonSerializer.Serialize(this, HostJson.Options));
}
internal sealed record ActionCatalogResult(string CatalogVersion, string? InstallationVersion, string[] Gaps,
    IReadOnlyList<ActionCatalogItem> Items, int Total, string? NextCursor, bool Truncated, string? Hint);
