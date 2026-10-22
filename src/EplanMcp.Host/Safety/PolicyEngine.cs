using EplanMcp.Contracts;

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
/// <see cref="HostConfig.WritesOutsideScratch"/> (M3 design Q5). A write that changes system master data (ADR-0019)
/// follows <see cref="HostConfig.MasterDataWrites"/>, is refused for a database on SQL Server, and otherwise always
/// needs approval, whatever its project; a project it changes as well follows the project rules on top.
/// </summary>
internal static class PolicyEngine
{
    public const string OutsideScratchDenied =
        "Writing to projects outside the scratch folders is not allowed by this server's safety policy. Work on a copy of the project in a scratch folder.";

    public const string CodeDisabled = "Tools that run code in EPLAN are disabled by this server's safety policy.";

    public const string MasterDataDenied =
        "Writing to EPLAN's system master data, such as the parts database, is not allowed by this server's safety policy. Nothing was changed.";

    public const string MasterDataServerDenied =
        "The parts database EPLAN uses is on SQL Server, and this server's safety policy allows writes to file databases only: a SQL Server database cannot be backed up from this computer and may be shared with others. Nothing was changed.";

    /// <summary>
    /// The verdict for a write of <paramref name="tier"/> to a project that is <paramref name="inScratch"/> (true when
    /// it changes no project) and, when not null, to <paramref name="masterData"/>.
    /// </summary>
    public static PolicyDecision Decide(ToolTier tier, bool inScratch, HostConfig config, MasterDataTarget? masterData = null) => tier switch
    {
        ToolTier.Read => new(PolicyVerdict.Allow),
        ToolTier.Code => new(PolicyVerdict.Deny, CodeDisabled),
        _ when masterData is not null && config.MasterDataWrites == MasterDataWrites.Deny => new(PolicyVerdict.Deny, MasterDataDenied),
        _ when masterData is { Server: true } => new(PolicyVerdict.Deny, MasterDataServerDenied),
        _ when !inScratch && config.WritesOutsideScratch == OutsideScratchWrites.Deny => new(PolicyVerdict.Deny, OutsideScratchDenied),
        ToolTier.Write when inScratch && masterData is null => new(PolicyVerdict.Allow),
        _ => new(PolicyVerdict.NeedsApproval),
    };
}
