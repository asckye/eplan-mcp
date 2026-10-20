namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IMacroInserter.Insert"/> reads back, inside the transaction.</summary>
public sealed class PlacedMacro
{
    /// <summary>The inserted functions with the tags EPLAN gave them, in EPLAN's order.</summary>
    public List<MacroFunctionRecord> Functions { get; set; } = [];

    /// <summary>How many objects EPLAN inserted in all.</summary>
    public int ObjectCount { get; set; }
}
