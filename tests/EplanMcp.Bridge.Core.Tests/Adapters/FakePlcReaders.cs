using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanDeviceReader
{
    public static Function? FindMain(DMObjectsFinder finder, string tag, Func<Function, bool> preferred)
    {
        using var filter = new FunctionsFilter { Name = tag, ExactNameMatching = true };
        return finder.GetFunctions(filter).FirstOrDefault(f => f.IsMainFunction && preferred(f));
    }
}

internal static class EplanConnectionReader
{
    public static IEnumerable<Connection> FunctionConnections(DMObjectsFinder finder, List<Function> functions) => [];
    public static Page? PageOf(Connection connection) => connection.Page;
}
