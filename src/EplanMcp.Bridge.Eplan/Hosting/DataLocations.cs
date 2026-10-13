namespace EplanMcp.Bridge.Eplan.Hosting;

/// <summary>
/// The data root of ADR-0008: <c>%EPLANMCP_HOME%</c> if set, otherwise <c>%USERPROFILE%\EplanMcp</c>, never
/// %LOCALAPPDATA% (MSIX redirection, api-notes A6). Uses BCL types only, so it works even when the Bridge's own
/// assemblies failed to load.
/// </summary>
internal static class DataLocations
{
    public static string Root =>
        Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");

    public static string Logs => Path.Combine(Root, "logs");
}
