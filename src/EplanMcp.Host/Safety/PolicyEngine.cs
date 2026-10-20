namespace EplanMcp.Host.Safety;

/// <summary>A tool's safety tier (ADR-0005 section 1), matching its MCP annotations.</summary>
internal enum ToolTier
{
    Read,
    Write,
    Destructive,
    Code,
}

/// <summary>What the policy lets a call do.</summary>
internal enum PolicyVerdict
{
    Allow,
    NeedsApproval,
    Deny,
}

/// <summary>The verdict, and for <see cref="PolicyVerdict.Deny"/> the message the model gets (no way around it).</summary>
internal sealed record PolicyDecision(PolicyVerdict Verdict, string? Message = null);

/// <summary>
/// The decision table of ADR-0005 as a pure function, covered case by case by Small tests. Destructive tools always
/// need approval, in scratch too; Code tools stay disabled in M3. Writes outside the scratch roots follow
/// <see cref="HostConfig.WritesOutsideScratch"/> (M3 design Q5).
/// </summary>
internal static class PolicyEngine
{
    public const string OutsideScratchDenied =
        "Writing to projects outside the scratch folders is not allowed by this server's safety policy. Work on a copy of the project in a scratch folder.";

    public const string CodeDisabled = "Tools that run code in EPLAN are disabled by this server's safety policy.";

    public static PolicyDecision Decide(ToolTier tier, bool inScratch, HostConfig config) => tier switch
    {
        ToolTier.Read => new(PolicyVerdict.Allow),
        ToolTier.Code => new(PolicyVerdict.Deny, CodeDisabled),
        _ when !inScratch && config.WritesOutsideScratch == OutsideScratchWrites.Deny => new(PolicyVerdict.Deny, OutsideScratchDenied),
        ToolTier.Write when inScratch => new(PolicyVerdict.Allow),
        _ => new(PolicyVerdict.NeedsApproval),
    };
}
