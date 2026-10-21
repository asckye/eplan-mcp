namespace EplanMcp.Host.Addin;

/// <summary>
/// The add-in DLL that install-addin registers, and the folder whose files lose their download mark first (M5 design
/// 4.2). Found next to the running eplanmcp.exe only, never through a setting or variable:
/// <list type="bullet">
/// <item>installed (architecture 7): <c>&lt;app&gt;\bridge\net481\EplanMcp.Bridge.Eplan.dll</c>, unmarking all of <c>&lt;app&gt;\bridge</c>;</item>
/// <item>development: the Host runs from <c>src\EplanMcp.Host\bin\&lt;configuration&gt;\&lt;tfm&gt;</c>, and the add-in of the
/// same configuration is <c>src\EplanMcp.Bridge.Eplan\bin\&lt;configuration&gt;\net481\EplanMcp.Bridge.Eplan.dll</c>,
/// unmarking that folder.</item>
/// </list>
/// The installed layout wins when both exist; there is no fallback to another configuration.
/// </summary>
internal sealed record AddinLocation(string Dll, string UnblockFolder)
{
    /// <summary>The add-in's assembly name: what <c>/unregister</c> takes, and the pipe owner's DLL.</summary>
    public const string AssemblyName = "EplanMcp.Bridge.Eplan";

    public const string FileName = AssemblyName + ".dll";

    /// <summary>The add-in for the Host in <paramref name="hostDirectory"/>, or null; <paramref name="searched"/> lists where it looked.</summary>
    public static AddinLocation? Find(string hostDirectory, out IReadOnlyList<string> searched)
    {
        string bridge = Path.Combine(hostDirectory, "bridge");
        var installed = new AddinLocation(Path.Combine(bridge, "net481", FileName), bridge);
        AddinLocation? development = Development(hostDirectory);
        searched = development is null ? [installed.Dll] : [installed.Dll, development.Dll];
        return File.Exists(installed.Dll) ? installed : development is not null && File.Exists(development.Dll) ? development : null;
    }

    private static AddinLocation? Development(string hostDirectory)
    {
        var tfm = new DirectoryInfo(Path.TrimEndingDirectorySeparator(hostDirectory));
        DirectoryInfo? configuration = tfm.Parent;
        DirectoryInfo? bin = configuration?.Parent;
        DirectoryInfo? project = bin?.Parent;
        if (configuration is null || project?.Parent is not { } src
            || !bin!.Name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || !project.Name.Equals("EplanMcp.Host", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string folder = Path.Combine(src.FullName, AssemblyName, "bin", configuration.Name, "net481");
        return new AddinLocation(Path.Combine(folder, FileName), folder);
    }
}
