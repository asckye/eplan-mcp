namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the page a drawing goes on and imports a DXF or DWG file onto it, implemented by the EPLAN adapter layer
/// (docs/tools/eplan_import_dxf.md, section 5). Main thread only.
/// </summary>
public interface IDrawingImporter
{
    /// <summary>The page whose full name is exactly <paramref name="pageName"/>, read as <see cref="IMacroInserter.ReadPage"/> reads it; null when there is none.</summary>
    public MacroTargetPage? ReadPage(string projectPath, string pageName);
}
