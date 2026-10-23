using System.IO;

namespace EplanMcp.Workbench.Commands;

/// <summary>
/// Where the <c>eplanmcp.exe</c> of this Workbench is, by the two layouts there are and nothing else: no setting, no PATH
/// and no environment variable, so the Workbench only ever runs the Host it was shipped or built with (M5 design 4.4).
/// <list type="bullet">
/// <item>Extracted ZIP (ADR-0016): the Workbench in <c>&lt;any folder&gt;\workbench\</c>, the Host in <c>&lt;any folder&gt;\eplanmcp.exe</c>.</item>
/// <item>Built from the repository (CLAUDE.md): the Workbench in <c>src\EplanMcp.Workbench\bin\&lt;configuration&gt;\&lt;tfm&gt;\</c>,
/// the Host of the same configuration in <c>src\EplanMcp.Host\bin\&lt;configuration&gt;\net10.0\</c>.</item>
/// </list>
/// </summary>
internal static class ProgramLocation
{
    public const string HostFileName = "eplanmcp.exe";

    /// <summary>The Host of the layout <paramref name="workbenchDirectory"/> is in, when that file exists.</summary>
    public static string? FindHost(string workbenchDirectory) =>
        HostBesideWorkbench(workbenchDirectory) is { } path && File.Exists(path) ? path : null;

    /// <summary>The Host path of the layout <paramref name="workbenchDirectory"/> is in; does not read the disk.</summary>
    public static string? HostBesideWorkbench(string workbenchDirectory)
    {
        var directory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(workbenchDirectory));
        return Named(directory, "workbench") && directory.Parent is { } parent
            ? Path.Combine(parent.FullName, HostFileName)
            : Built(directory);
    }

    private static string? Built(DirectoryInfo framework)
    {
        DirectoryInfo? configuration = framework.Parent, bin = configuration?.Parent, project = bin?.Parent;
        return configuration is not null && Named(bin, "bin") && Named(project, "EplanMcp.Workbench") && project!.Parent is { } source
            ? Path.Combine(source.FullName, "EplanMcp.Host", "bin", configuration.Name, "net10.0", HostFileName) : null;
    }

    private static bool Named(DirectoryInfo? directory, string name) => directory?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true;
}
