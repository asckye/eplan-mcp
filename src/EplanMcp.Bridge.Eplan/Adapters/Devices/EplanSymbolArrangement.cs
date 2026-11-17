using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanSymbolArrangement
{
    public static string[]? Available(SymbolReference symbol)
    { try { return symbol.PropertyPlacementsSchemas.All.Select(s => s.Name).ToArray(); } catch (Exception ex) when (ex is BaseException or InvalidOperationException) { return null; } }
    public static string? SelectedName(SymbolReference symbol)
    { try { return symbol.PropertyPlacementsSchemas.Selected.Name; } catch (Exception ex) when (ex is BaseException or InvalidOperationException or NullReferenceException) { return null; } }
    public static void Select(SymbolReference symbol, string name)
    {
        var schemas = symbol.PropertyPlacementsSchemas;
        var selected = schemas.All.Where(s => string.Equals(s.Name, name, StringComparison.Ordinal)).ToArray();
        if (selected.Length == 0)
        { throw WriteArguments.Invalid(["Unknown property arrangement. Available names: " + string.Join(", ", schemas.All.Select(s => s.Name))]); }
        schemas.Selected = selected[0];
        if (SelectedName(symbol) is { } actual && actual != name)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Property arrangement readback differs from the requested name."); }
    }
    public static string? ReadActive(UniversalPropertyList properties)
    {
        try
        {
            using AnyPropertyId id = 19307;
            if (!properties.Exists(id))
            { return null; }
            using PropertyValue value = properties[id];
            return value.IsEmpty ? null : value.ToString();
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or InvalidOperationException) { return null; }
    }
}
