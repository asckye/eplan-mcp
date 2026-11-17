namespace EplanMcp.Contracts;

/// <summary>Read-only step guard inside invoke; resources and binding are freshly checked by the Host.</summary>
public sealed class PlanStepGuardArgs
{
    /// <summary>Explicit open project name or path.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The complete read scope; requires a matching members precondition.</summary>
    public PlanGuardScope ReadScope { get; set; } = new();

    /// <summary>The plan's complete closed precondition list.</summary>
    public List<PlanGuardCondition> Preconditions { get; set; } = [];

    /// <summary>Actual resource SHA-256 values freshly recomputed by the Host, never the expected values copied back.</summary>
    public List<string> ResourceHashes { get; set; } = [];

    /// <summary>Actual workspace revision read by the Host under its binding lock, or null without a binding.</summary>
    public long? BindingRevision { get; set; }

    /// <summary>Actual canonical workspace lock digest, or null without a binding.</summary>
    public string? LockDigest { get; set; }
}
