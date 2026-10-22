namespace EplanMcp.Packs.Scanning;

/// <summary>What a scan found: the inputs by name, the resources sorted by pack path, and the findings.</summary>
public sealed record ScanResult(IReadOnlyList<PackInput> Inputs, IReadOnlyList<ScannedResource> Resources, IReadOnlyList<ScanFinding> Findings)
{
    public bool HasErrors => Findings.Any(f => f.Severity == FindingSeverity.Error);
}
