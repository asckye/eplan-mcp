namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the page a drawing goes on and imports a DXF or DWG file onto it, implemented by the EPLAN adapter layer
/// (docs/tools/eplan_import_dxf.md, section 5). Main thread only; <see cref="ImportDxf"/> only inside the write's
/// transaction, whose SafetyPoint takes the import back (spike S22, api-notes A29).
/// </summary>
public interface IDrawingImporter
{
    /// <summary>The page whose full name is exactly <paramref name="pageName"/>, read as <see cref="IMacroInserter.ReadPage"/> reads it; null when there is none.</summary>
    public MacroTargetPage? ReadPage(string projectPath, string pageName);

    /// <summary>
    /// Imports the file at <paramref name="filePath"/> onto the page with <c>Import.DXFPage</c>, scaled to the page
    /// (<paramref name="fit"/>) or not, in its middle (<paramref name="center"/>) or at its lower left, with EPLAN's
    /// default import scheme. Returns how many placements the page has more than before. Throws EPLAN_OPERATION_FAILED
    /// when EPLAN refuses the file.
    /// </summary>
    public int ImportDxf(string projectPath, string pageName, string filePath, bool fit, bool center);
}
