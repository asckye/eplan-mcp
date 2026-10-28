using Eplan.EplApi.DataModel;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanProjects
{
    [ThreadStatic]
    public static Project? Project;
    public static Project ByPath(string path)
    {
        Assert.Equal("project", path);
        return Project!;
    }
}

internal static class MainThread
{
    public static void Assert() { }
}

internal static class EplanPropertyReadAdapter
{
    public static IEnumerable<Function> Functions(DMObjectsFinder finder, Action check)
    {
        foreach (Page page in finder.GetPages(null))
        {
            check();
            foreach (Function f in page.AllPlacements.OfType<Function>().Concat(page.Functions))
            { yield return f; }
        }
    }
}
