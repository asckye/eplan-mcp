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
