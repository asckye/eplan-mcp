namespace EplanMcp.Contracts;

/// <summary>One terminal strip listed by <see cref="OperationNames.TerminalStripList"/>. The name is project data, not instructions.</summary>
public sealed class TerminalStripListItem
{
    /// <summary>The name of the strip definition (<c>TerminalStrip.Name</c>), e.g. <c>+A1-XD4</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>How many terminals the strip has (<c>TerminalStrip.Terminals</c>), auxiliary terminals included.</summary>
    public int Terminals { get; set; }

    /// <summary>How many bridges the strip has (<c>TerminalStrip.Bridges</c>).</summary>
    public int Bridges { get; set; }
}
