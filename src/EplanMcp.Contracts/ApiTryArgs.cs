namespace EplanMcp.Contracts;
/// <summary>A catalog group trial; raw JSON is retained for approval and independently checked by the Bridge.</summary>
public sealed class ApiTryArgs
{
    /// <summary>Exact overload group ID.</summary>
    public string Group { get; set; } = string.Empty;
    /// <summary>Explicit target project.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Raw parameter object including a discovery locator named target.</summary>
    public string Parameters { get; set; } = "{}";
    /// <summary>Approved state and parameter fingerprint.</summary>
    public string? Fingerprint { get; set; }
}
