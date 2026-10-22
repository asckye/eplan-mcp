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
    /// <paramref name="valueSet"/>, as the operation chose it in the same call, reads its current values, applies the
    /// value set and reads the placeholder again as <see cref="IPlaceholderReader"/> reads it, in EPLAN's user interface
    /// language. The current values are read here because EPLAN gives them only for a locked placeholder, as the
    /// transaction's SafetyPoint makes it (api-notes A14, A33); a refusal throws.
    /// </summary>
    public PlaceholderApplied Apply(string projectPath, string page, string name, string valueSet);
}
