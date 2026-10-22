using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Counts and a fingerprint of page/function identities and names, entries, project parts and references.</summary>
public sealed class ListImportState(ListImportCounts counts, string fingerprint)
{
    public ListImportCounts Counts { get; } = counts;
    public string Fingerprint { get; } = fingerprint;
}
