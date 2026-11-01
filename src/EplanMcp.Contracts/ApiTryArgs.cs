namespace EplanMcp.Contracts;
/// <summary>A catalog group trial; raw JSON is retained for approval and independently checked by the Bridge.</summary>
public sealed class ApiTryArgs
{
    /// <summary>Exact overload group ID.</summary>
    public string Group { get; set; } = string.Empty;
    /// <summary>Explicit target project.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Raw parameter object including a discovery locator or session-scoped handle named target.</summary>
    public string Parameters { get; set; } = "{}";
    /// <summary>Approved state and parameter fingerprint.</summary>
    public string? Fingerprint { get; set; }
    /// <summary>Trusted Host data root, supplied internally rather than by tool parameters.</summary>
    public string DataRoot { get; set; } = string.Empty;
    /// <summary>Host-allocated output directory identity, bound to the approved fingerprint.</summary>
    public string OutputId { get; set; } = string.Empty;

    /// <summary>Host-created process/session identity; never supplied by the model.</summary>
    public string HostSession { get; set; } = string.Empty;
    /// <summary>Canonical scratch path authorized by the Host for this call.</summary>
    public string ScratchPath { get; set; } = string.Empty;
    /// <summary>Bridge-verified handle provenance for approval and audit.</summary>
    public string HandleSources { get; set; } = string.Empty;
}
