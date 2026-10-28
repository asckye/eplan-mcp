using EplanMcp.Contracts;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Packs;

internal sealed record ModuleInspectResult(ModuleInspection? Inspection, IReadOnlyList<ScanFinding> Gaps, BridgeError? Problem)
{
    public bool Complete => Problem is null && Gaps.Count == 0 && Inspection?.Complete == true;
}
