using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Stands in for the native NameService call; the linked adapters only need it to exist.</summary>
internal static class EplanVisibleNames
{
    public static void Adjust(Page page, object function) => _ = (page, function);
}
