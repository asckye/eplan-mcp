namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.TerminalStripGet"/> (docs/tools/eplan_terminal_strip_get.md); the slice is of the terminals.</summary>
public sealed class TerminalStripGetArgs : ListArgs
{
    /// <summary>The strip's full name, e.g. <c>+A1-XD4</c>, or its name without structure (<c>-XD4</c>) when one strip has it.</summary>
    public string? Strip { get; set; }
}
