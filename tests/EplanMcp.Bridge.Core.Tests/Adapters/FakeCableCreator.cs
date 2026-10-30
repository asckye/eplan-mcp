using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

// The linked cable adapter uses its real discovery and readback code against scope-bound fake handles.
internal sealed partial class EplanConnectionCreator : ICableDefinitionWriter
{
    private static Page Page(string project, string page)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        using var filter = new PagesFilter { Name = page, ExactNameMatching = true };
        return finder.GetPages(filter).Single(p => p.Name == page);
    }
    private static void AssertCurrent(string project) => Assert.NotNull(EplanProjects.ByPath(project));
    private static string Key(SymbolReference function, int index) => FormattableString.Invariant($"17/{function.ObjectIdentifier}/{index}");
}

internal static class EplanConnectionReader
{
    public static Page PageOf(Connection connection) => connection.Page;
}
