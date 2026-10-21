using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A terminal strip as <see cref="ITerminalReader.ReadStrip"/> reads it: EPLAN's values, unchanged.</summary>
public sealed class TerminalStripRecord
{
    public TerminalStripListItem Strip { get; set; } = new();

    /// <summary>Every terminal with every connection end and bridged terminal EPLAN gives.</summary>
    public List<TerminalItem> Terminals { get; set; } = [];
}
