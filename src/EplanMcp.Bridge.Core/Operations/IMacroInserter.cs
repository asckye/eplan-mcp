namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the page a window macro goes on and inserts the macro, implemented by the EPLAN adapter layer
/// (docs/tools/eplan_macro_insert.md, section 5). Main thread only; <see cref="Insert"/> only inside the write's
/// transaction, whose SafetyPoint is the only wrapper that takes an insert back (api-notes A13).
/// </summary>
public interface IMacroInserter
{
    /// <summary>
    /// The page whose full name is exactly <paramref name="pageName"/> (EPLAN's exact name filter), with its type, its
    /// size when EPLAN can tell it, and the objects on it; null when there is none.
    /// </summary>
    public MacroTargetPage? ReadPage(string projectPath, string pageName);

    /// <summary>
    /// Inserts variant <paramref name="variant"/> of the <c>MultiLine</c> representation of the window macro at
    /// <paramref name="macroPath"/>, a full path <see cref="MacroFiles.Resolve"/> checked, on the page, the macro
    /// outline's top left corner at (<paramref name="x"/>, <paramref name="y"/>) (A14). With <paramref name="number"/>
    /// EPLAN gives the devices tags no device has (<c>NumerationMode.Number</c>), otherwise they keep the macro's tags
    /// (<c>None</c>, S10). Returns what EPLAN inserted. Throws INVALID_ARGUMENT when EPLAN refuses to place the same
    /// symbols on top of each other ("S029181", A13).
    /// </summary>
    public PlacedMacro Insert(string projectPath, string pageName, string macroPath, int variant, double x, double y, bool number);
}
