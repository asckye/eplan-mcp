using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Customer redraw 2026-11-02: objects created through the API keep an empty visible name (S41), so the symbol's own
/// DT texts (cable and box 20010, PLC point 20010) show nothing. A95 also confirms interruption points need this
/// adjustment after assigning their full name; a false result is not a write failure.
/// </summary>
internal static class EplanVisibleNames
{
    /// <summary>Lets EPLAN derive the visible name and its format from the full name, as the editor does.</summary>
    public static void Adjust(Page page, FunctionBase function)
    {
        using var names = new NameService();
        names.AdjustVisibleName(page, function);
    }
}
