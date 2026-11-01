namespace EplanMcp.Contracts;
/// <summary>Detached trial observations; equal snapshots do not prove absence of side effects.</summary>
public sealed class ApiTryData
{
    /// <summary>Scalar result, formatted invariantly.</summary>
    public string? Value { get; set; }
    /// <summary>A returned object's discovery locator, when the result is not scalar.</summary>
    public DrawingLocator? Locator { get; set; }
    /// <summary>Borrowed object reference scoped to this Host session and EPLAN process.</summary>
    public ApiObjectHandle? Handle { get; set; }
    /// <summary>Catalog group that produced the handle; audit metadata contains no object contents.</summary>
    public string? HandleSource { get; set; }
    /// <summary>A committed transaction cleanup or undo warning, if any.</summary>
    public string? Warning { get; set; }
    /// <summary>Every output artifact, with size and content hash.</summary>
    public List<ApiTryFile> Files { get; set; } = [];
    /// <summary>Business page hashes, counts and design-file fingerprints before execution.</summary>
    public Dictionary<string, string> Before { get; set; } = [];
    /// <summary>The same observations after execution.</summary>
    public Dictionary<string, string> After { get; set; } = [];
}
