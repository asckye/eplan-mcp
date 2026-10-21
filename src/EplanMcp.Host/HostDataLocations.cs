namespace EplanMcp.Host;

/// <summary>
/// The data root and what lies in it (ADR-0008, architecture 7): <c>EPLANMCP_HOME</c>, or <c>%USERPROFILE%\EplanMcp</c>,
/// the same rule as the add-in's DataLocations. Never under %LOCALAPPDATA%, which MSIX clients redirect (spike S5).
/// </summary>
internal sealed class HostDataLocations(string root)
{
    public string Root { get; } = root;

    /// <summary>The configuration; optional, defaults apply without it (architecture 8.9).</summary>
    public string ConfigFile => Path.Combine(Root, "config.json");

    /// <summary>The scratch root when the configuration names none.</summary>
    public string DefaultScratch => Path.Combine(Root, "scratch");

    public string Audit => Path.Combine(Root, "audit");

    /// <summary>Write suspensions (ADR-0010, M3 design 4.3).</summary>
    public string State => Path.Combine(Root, "state");

    /// <summary>The export root: one new folder per export below it, never configurable (SEC-20, M4 design Q8).</summary>
    public string Exports => Path.Combine(Root, "exports");

    /// <summary>The copies made before Destructive writes (ADR-0013); never deleted by EplanMcp.</summary>
    public string Backups => Path.Combine(Root, "backups");

    /// <summary>The import root: the import tools read files below it only, never configurable (SEC-31, M4c design Q4).</summary>
    public string Imports => Path.Combine(Root, "imports");

    public static HostDataLocations FromEnvironment() => new(
        Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp"));
}
