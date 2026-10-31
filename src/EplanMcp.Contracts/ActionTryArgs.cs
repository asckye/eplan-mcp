namespace EplanMcp.Contracts;

/// <summary>One catalog action validated by the Host; no command line or caller-selected execution target.</summary>
public sealed class ActionTryArgs
{
    public string Action { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string Project { get; set; } = string.Empty;
    public List<ActionParameter> Parameters { get; set; } = [];
    public string? Fingerprint { get; set; }
}
