namespace EplanMcp.Contracts;
/// <summary>Detached trial observations; equal snapshots do not prove absence of side effects.</summary>
public sealed class ApiTryData
{
    /// <summary>Scalar result, formatted invariantly.</summary>
    public string? Value { get; set; }
    /// <summary>Business page hashes, counts and design-file fingerprints before execution.</summary>
    public Dictionary<string, string> Before { get; set; } = [];
    /// <summary>The same observations after execution.</summary>
    public Dictionary<string, string> After { get; set; } = [];
}
