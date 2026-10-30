using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanConnectionReader
{
    public static IEnumerable<Connection> FunctionConnections(DMObjectsFinder finder, List<Function> functions) => [];
    public static Page? PageOf(Connection connection) => connection.Page;
}
