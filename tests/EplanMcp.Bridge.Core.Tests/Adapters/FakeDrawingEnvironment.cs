using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed class EplanExceptionMapper : IExceptionMapper
{
    public EplanMcp.Contracts.BridgeError? Map(Exception exception) => null;
}

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

internal static class EplanObjectReader
{
    internal static EplanMcp.Contracts.SnapshotObject Describe(StorableObject obj, string kind)
    {
        var placement = Assert.IsAssignableFrom<Placement>(obj);
        return new() { Type = placement.IdentifierType, Oid = placement.ObjectIdentifier, Kind = kind, Page = placement.Page?.Name, Name = "" };
    }
}

internal sealed class EplanProjectLanguages
{
    private readonly Project _project = EplanProjects.Project!;
    public LanguageSettings Read(string projectPath)
    {
        Assert.Same(_project, EplanProjects.ByPath(projectPath));
        if (_project.DisplayError is { } error)
        { throw error; }
        return new() { Display = _project.Display };
    }
}

internal sealed partial class EplanConnectionCreator(bool readSymbolBounds = true)
{
    public PageScene Read(string project, string page) => throw new NotSupportedException(readSymbolBounds.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    /// <summary>The fake finder's unfiltered read is complete; the native gap (A170) exists only in EPLAN.</summary>
    public static Function[] AllFunctions(DMObjectsFinder finder) => [.. EplanNativePreconditions.Valid(finder.GetFunctions(null))];

    /// <summary>The fake name filter is complete, so it alone answers.</summary>
    public static Function[] NamedFunctions(DMObjectsFinder finder, string name)
    {
        using var filter = new FunctionsFilter { Name = name, ExactNameMatching = true };
        return [.. EplanNativePreconditions.Valid(finder.GetFunctions(filter))];
    }
}
