namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the page a device goes on and places a part's device there, implemented by the EPLAN adapter layer
/// (docs/tools/eplan_device_place.md, section 5). Main thread only.
/// </summary>
public interface IDevicePlacer
{
    /// <summary>The page whose full name is exactly <paramref name="pageName"/>, read as <see cref="IMacroInserter.ReadPage"/> reads it; null when there is none.</summary>
    public MacroTargetPage? ReadPage(string projectPath, string pageName);
}
