namespace EplanMcp.Large.ConnectionHelper;

/// <summary>
/// The data root of the Large run this EPLAN serves: EPLANMCP_HOME when the slot set it (two EPLAN instances side by side,
/// tests/EplanMcp.Large.Tests/README.md), otherwise %USERPROFILE%\EplanMcp.
/// </summary>
internal static class DataRoot
{
    internal static string Path => Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } home
        ? home
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
}
