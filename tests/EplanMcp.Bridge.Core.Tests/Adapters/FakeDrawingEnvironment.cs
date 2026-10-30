using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

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

internal sealed class EplanProjectLanguages
{
    private readonly Project _project = EplanProjects.Project!;
    public LanguageSettings Read(string projectPath)
    {
        Assert.Same(_project, EplanProjects.ByPath(projectPath));
        return new() { Display = _project.Display };
    }
}

internal sealed partial class EplanConnectionCreator(bool readSymbolBounds = true)
{
    public ConnectionCreatePlanner.Scene Read(string project, string page) => throw new NotSupportedException(readSymbolBounds.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

internal static class EplanDeviceReader
{
    public static Page? FindPage(DMObjectsFinder finder, string page)
    {
        using var filter = new PagesFilter { Name = page, ExactNameMatching = true };
        return finder.GetPages(filter).SingleOrDefault(p => p.Name == page);
    }

    public static Function? FindMain(DMObjectsFinder finder, string tag, Func<Function, bool> preferred)
    {
        using var filter = new FunctionsFilter { Name = tag, ExactNameMatching = true };
        return finder.GetFunctions(filter).FirstOrDefault(f => f.IsMainFunction && preferred(f));
    }
}
