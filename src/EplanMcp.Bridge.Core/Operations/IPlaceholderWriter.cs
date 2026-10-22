namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Applies a value set to one placeholder for <see cref="PlaceholderApplyExecuteOperation"/>
/// (docs/tools/eplan_placeholder_apply.md, section 5), implemented by the EPLAN adapter layer. Main thread only, inside
/// the transaction the operation opened; EPLAN's failures are thrown and roll the transaction back.
/// </summary>
public interface IPlaceholderWriter
{
    /// <summary>
    /// Finds the one placeholder on <paramref name="page"/> named <paramref name="name"/> that has
    /// <paramref name="valueSet"/>, as the operation chose it in the same call, applies the value set and returns the
    /// placeholder read again as <see cref="IPlaceholderReader"/> reads it, in EPLAN's user interface language.
    /// </summary>
    public PlaceholderRecord Apply(string projectPath, string page, string name, string valueSet);
}
