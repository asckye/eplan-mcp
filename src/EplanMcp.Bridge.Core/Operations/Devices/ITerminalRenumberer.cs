namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and numbers terminals for eplan_terminal_renumber (docs/tools/eplan_terminal_renumber.md, section 5),
/// implemented by the EPLAN adapter layer. Main thread only; <see cref="Renumber"/> only inside the write's transaction.
/// </summary>
public interface ITerminalRenumberer
{
    /// <summary>The names of the schemes under the user setting <c>USER.Terminal.Numbering.Scheme</c>, in EPLAN's order (A24).</summary>
    public IReadOnlyList<string> Schemes();

    /// <summary>
    /// The terminals of every strip named exactly <paramref name="strip"/> (ordinal), or of the whole project when it is
    /// null, each once, with their session identifiers; null when no strip has that name.
    /// </summary>
    public IReadOnlyList<PlacedFunction>? ReadTerminals(string projectPath, string? strip);

    /// <summary>
    /// Lets EPLAN number the terminals of <paramref name="strips"/> (null: the whole project) with
    /// <c>Renumber.Terminals</c> in <paramref name="scheme"/>, from <paramref name="start"/> in steps of
    /// <paramref name="increment"/>: only designations with <c>?</c> when <paramref name="onlyUnnumbered"/>. A scheme
    /// EPLAN refuses throws INVALID_ARGUMENT, other EPLAN failures EPLAN_OPERATION_FAILED.
    /// </summary>
    public void Renumber(string projectPath, IReadOnlyList<string>? strips, bool onlyUnnumbered, string scheme, int start, int increment);
}
