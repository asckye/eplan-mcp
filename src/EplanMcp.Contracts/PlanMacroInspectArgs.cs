namespace EplanMcp.Contracts;

/// <summary>Read-only arguments of <see cref="OperationNames.PlanMacroInspect"/>.</summary>
public sealed class PlanMacroInspectArgs
{
    /// <summary>A relative .ema path below $(MD_MACROS), as macro.list returns it.</summary>
    public string? Macro { get; set; }
}
