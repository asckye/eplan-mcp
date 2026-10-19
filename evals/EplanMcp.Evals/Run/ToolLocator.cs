namespace EplanMcp.Evals.Run;

/// <summary>Finds the repository, claude and eplanmcp when the command line does not name them (design 4.4).</summary>
internal static class ToolLocator
{
    private static readonly string[] _configurations = ["Release", "Debug"];

    /// <summary>The nearest directory at or above <paramref name="start"/> that holds <c>EplanMcp.slnx</c>.</summary>
    public static string? RepositoryRoot(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>claude.exe</c> on <paramref name="searchPath"/> (the PATH), otherwise the newest copy the Claude desktop app
    /// keeps under <c>%APPDATA%\Claude\claude-code\&lt;version&gt;\&lt;build&gt;\</c>.
    /// </summary>
    public static string? Claude(string? searchPath, string appData)
    {
        foreach (string directory in (searchPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory, "claude.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var versions = new DirectoryInfo(Path.Combine(appData, "Claude", "claude-code"));
        return !versions.Exists
            ? null
            : versions.EnumerateDirectories()
                .Select(d => (Directory: d, Version: Version.TryParse(d.Name, out Version? v) ? v : null))
                .Where(d => d.Version is not null)
                .OrderByDescending(d => d.Version)
                .SelectMany(d => d.Directory.EnumerateFiles("claude.exe", SearchOption.AllDirectories))
                .Select(f => f.FullName)
                .FirstOrDefault();
    }

    /// <summary>The Release build of eplanmcp in the repository, otherwise the Debug build (CLAUDE.md, common commands).</summary>
    public static string? Eplanmcp(string? repositoryRoot) =>
        repositoryRoot is null
            ? null
            : _configurations
                .Select(configuration => Path.Combine(repositoryRoot, "src", "EplanMcp.Host", "bin", configuration, "net10.0", "eplanmcp.exe"))
                .FirstOrDefault(File.Exists);
}
