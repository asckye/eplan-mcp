namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the page a device goes on and places a part's device there, implemented by the EPLAN adapter layer
/// (docs/tools/eplan_device_place.md, section 5). Main thread only; <see cref="Place"/> only inside the write's
/// transaction, whose SafetyPoint takes the placement back (spike S20, api-notes A26).
/// </summary>
public interface IDevicePlacer
{
    /// <summary>The page whose full name is exactly <paramref name="pageName"/>, read as <see cref="IMacroInserter.ReadPage"/> reads it; null when there is none.</summary>
    public MacroTargetPage? ReadPage(string projectPath, string pageName);

    /// <summary>
    /// Places variant <paramref name="variant"/> of part <paramref name="part"/> on the page at (<paramref name="x"/>,
    /// <paramref name="y"/>) with <c>DeviceService.CreateDevice</c>, which inserts the part's macro and returns all the
    /// device's functions, tagged <c>=+-Q1</c> (S20); gives each of them the name <paramref name="tag"/>, the main function
    /// first (A13), and reads back. Throws EPLAN_OPERATION_FAILED when EPLAN refuses.
    /// </summary>
    public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag);
}
