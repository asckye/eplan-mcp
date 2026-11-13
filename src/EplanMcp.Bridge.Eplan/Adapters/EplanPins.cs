using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a pin's connection designation (20022) without letting one unreadable pin fail a whole page.
/// <c>Pin.Name</c> throws <c>EmptyPropertyException</c> (S063111) when the pin carries no designation, for example a
/// library symbol placed without one (live 2026-11-08, MX_Dryer11 =GAA/1: connection.generate failed for the whole
/// project), and <c>IndexedPropertyException</c> (S063172) when the designation is indexed, which made
/// <c>eplan_page_review</c> return INTERNAL for the page (live 2026-11-13, the 16-page redraw).
/// </summary>
internal static class EplanPins
{
    internal static string? Name(Pin? pin)
    {
        try
        { return pin?.Name; }
        catch (Exception failure) when (failure is EmptyPropertyException or IndexedPropertyException)
        {
            Core.Operations.WriteExceptionDiagnostics.Report(failure);
            return string.Empty;
        }
    }
}
