using System.IO;

namespace EplanMcp.Workbench.Commands;

/// <summary>
/// Where the <c>eplanmcp.exe</c> of this Workbench is, by the two layouts there are and nothing else: no setting, no PATH
/// and no environment variable, so the Workbench only ever runs the Host it was installed or built with (M5 design 4.4).
/// <list type="bullet">
/// <item>Installed (ADR-0016, M5 design 4.1): the Workbench in <c>app\workbench\</c>, the Host at <c>app\eplanmcp.exe</c>.</item>
/// <item>Built from the repository (CLAUDE.md): the Workbench in <c>src\EplanMcp.Workbench\bin\&lt;configuration&gt;\&lt;tfm&gt;\</c>,
/// the Host of the same configuration in <c>src\EplanMcp.Host\bin\&lt;configuration&gt;\net10.0\</c>.</item>
/// </list>
/// </summary>
internal static class HostLocator
{
    public const string FileName = "eplanmcp.exe";

    /// <summary>The Host's target framework, the last folder of its build output.</summary>
    private const string HostFramework = "net10.0";

    /// <summary>The Host of the layout <paramref name="workbenchDirectory"/> is in, installed first; null when neither has one.</summary>
    public static string? Find(string workbenchDirectory) =>
        new[] { Installed(workbenchDirectory), Built(workbenchDirectory) }.FirstOrDefault(path => path is not null && File.Exists(path));

    /// <summary><c>app\eplanmcp.exe</c> for the Workbench in <c>app\workbench\</c>; null when the folders are not so named.</summary>
    public static string? Installed(string workbenchDirectory)
    {
        var workbench = new DirectoryInfo(Path.TrimEndingDirectorySeparator(workbenchDirectory));
        return Named(workbench, "workbench") && Named(workbench.Parent, "app") ? Path.Combine(workbench.Parent!.FullName, FileName) : null;
    }

    /// <summary>The Host built beside this Workbench in the repository; null when the Workbench is not in a build output.</summary>
    public static string? Built(string workbenchDirectory)
    {
        var framework = new DirectoryInfo(Path.TrimEndingDirectorySeparator(workbenchDirectory));
        DirectoryInfo? configuration = framework.Parent;
        DirectoryInfo? bin = configuration?.Parent;
        DirectoryInfo? project = bin?.Parent;
        return configuration is not null && Named(bin, "bin") && Named(project, "EplanMcp.Workbench") && project!.Parent is { } source
            ? Path.Combine(source.FullName, "EplanMcp.Host", "bin", configuration.Name, HostFramework, FileName)
            : null;
    }

    private static bool Named(DirectoryInfo? directory, string name) => directory?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true;
}
