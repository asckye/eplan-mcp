namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.TerminalStripList"/> (docs/tools/eplan_terminal_strip_list.md).</summary>
public sealed class TerminalStripListArgs : ListArgs
{
    /// <summary>Text the strip's name contains, ignoring case; null or blank means every strip.</summary>
    public string? NameContains { get; set; }
}
