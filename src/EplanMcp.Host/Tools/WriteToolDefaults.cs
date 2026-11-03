using System.Text.Json;

namespace EplanMcp.Host.Tools;

/// <summary>Preview defaults for tools migrated under ADR-0049.</summary>
internal static class WriteToolDefaults
{
    internal const bool Preview = true;
    internal const string Description = "Omit/true previews; false executes.";
    internal const string ExecutionHint = "Call again with dry_run: false to execute.";

    internal static bool IsPreview(JsonElement input) =>
        !input.TryGetProperty("dry_run", out JsonElement dry) || dry.ValueKind != JsonValueKind.False;
}
