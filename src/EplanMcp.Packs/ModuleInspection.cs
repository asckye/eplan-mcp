using System.Text.Json;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs;

/// <summary>Detached native evidence and declared metadata; completeness is limited to the explicit coverage.</summary>
public sealed record ModuleInspection(JsonElement Evidence, JsonElement? Declarations, IReadOnlyList<ScanFinding> Gaps)
{
    public bool Complete => Gaps.Count == 0;
}
