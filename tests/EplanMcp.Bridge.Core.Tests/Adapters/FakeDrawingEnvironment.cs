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
