using System.IO;

namespace EplanMcp.Workbench.Settings;

/// <summary>
/// The data root (ADR-0008), by the Host's rule: <c>EPLANMCP_HOME</c>, else <c>%USERPROFILE%\EplanMcp</c>. The pipe name
/// is derived from it, so a Workbench and the Hosts of one data root find each other (ADR-0014 decision 2).
/// </summary>
internal sealed class WorkbenchDataLocations(string root)
{
    public string Root { get; } = root;

    /// <summary>The Workbench's own settings (M5 design 4.1).</summary>
    public string Settings => Path.Combine(Root, "workbench", "settings.json");

    /// <summary>The Host's configuration, which the settings drawer shows and never writes (architecture 8.9, NAV-10).</summary>
    public string Config => Path.Combine(Root, "config.json");

    /// <summary>The Host's audit log, which the audit page reads and never writes (M5 design 4.4).</summary>
    public string Audit => Path.Combine(Root, "audit");

    /// <summary>The logs, shared with the Host (architecture 7); the Workbench's files are workbench-*.log.</summary>
    public string Logs => Path.Combine(Root, "logs");

    public static WorkbenchDataLocations FromEnvironment() => new(
        Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp"));
}
