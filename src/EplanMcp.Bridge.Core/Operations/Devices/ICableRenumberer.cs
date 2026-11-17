namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and numbers cables for eplan_cable_renumber (docs/tools/eplan_cable_renumber.md, section 5), implemented by the
/// EPLAN adapter layer. Main thread only; <see cref="Renumber"/> only inside the write's transaction.
/// </summary>
public interface ICableRenumberer
{
    /// <summary>The names of the schemes under the project setting <c>CableLog.CableNumberingScheme</c>, in EPLAN's order (A24).</summary>
    public IReadOnlyList<string> Schemes(string projectPath);

    /// <summary>
    /// The functions of category Cable, by category as S21 read them rather than through the cable navigator's filter
    /// (A24), with their session identifiers, in EPLAN's order.
    /// </summary>
    public IReadOnlyList<PlacedFunction> ReadCables(string projectPath);

    /// <summary>
    /// Lets EPLAN number the cables whose full tag is one of <paramref name="cables"/> (null: every cable) with
    /// <c>CableService.RenumberCables</c> in <paramref name="scheme"/>, from <paramref name="start"/> in steps of
    /// <paramref name="increment"/>, keeping existing tags when <paramref name="keepExisting"/>. A scheme EPLAN refuses
    /// throws INVALID_ARGUMENT, other EPLAN failures EPLAN_OPERATION_FAILED.
    /// </summary>
    public void Renumber(string projectPath, IReadOnlyList<string>? cables, string scheme, bool keepExisting, int start, int increment);
}
