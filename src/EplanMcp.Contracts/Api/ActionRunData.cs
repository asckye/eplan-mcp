namespace EplanMcp.Contracts;

/// <summary>Bridge action result; the Host shapes fingerprints and validates/hashes export files.</summary>
public sealed class ActionRunData
{
    /// <summary>Resolved project or configured database identifier; the Host redacts local paths.</summary>
    public string Target { get; set; } = string.Empty;
    /// <summary>The complete actual typed-context parameters; the Host renders paths relative to their roots.</summary>
    public List<ActionParameter> Parameters { get; set; } = [];
    /// <summary>Full fingerprint: count:kind maps to integer/null text; file:relative to bytes, tab, UTC; target/layer:*/project:type guard state; function:text:* evidence and synchronize:functionTextHash.</summary>
    public Dictionary<string, string> Before { get; set; } = [];
    /// <summary>Full post-execution fingerprint; null for a plan.</summary>
    public Dictionary<string, string>? After { get; set; }
    /// <summary>At most twenty newly added system messages.</summary>
    public List<string> EplanMessages { get; set; } = [];
    /// <summary>Total newly added message count, including omitted messages.</summary>
    public int EplanMessageCount { get; set; }
    /// <summary>Fingerprint/undo limitations and any post-commit warning.</summary>
    public string? Warning { get; set; }
}
