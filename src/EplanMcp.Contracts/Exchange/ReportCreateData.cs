namespace EplanMcp.Contracts;

/// <summary>Report page inventories read with fresh wrappers after generation and commit (A84).</summary>
public sealed class ReportCreateData
{
    /// <summary>All report pages matching the requested type and form after generation.</summary>
    public List<string> Pages { get; set; } = [];
    /// <summary>Report pages whose names were absent before generation.</summary>
    public List<string> NewPages { get; set; } = [];
    /// <summary>Transaction completion warning, when any.</summary>
    public string? Warning { get; set; }
}
