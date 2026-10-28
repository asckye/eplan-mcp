using EplanMcp.Contracts;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Packs;

/// <summary>Detached extraction evidence; missing capabilities produce gaps and no facts, never partial success.</summary>
internal sealed record ReferenceExtractionResult(IReadOnlyList<PackFact> Facts, IReadOnlyList<ScanFinding> Gaps, BridgeError? Problem = null)
{
    public static ReferenceExtractionResult Missing(string resource, string capability, string hint) => new([], [new(FindingSeverity.Gap, "capability_missing", resource, hint)],
        new BridgeError { Reason = ErrorReason.CapabilityMissing, Message = hint, Metadata = new() { ["missing"] = capability } });
}
