namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// The local stdio server every entry starts (M5 design 4.7, 4.11): <c>eplanmcp.exe serve</c>, named <c>eplan</c> like
/// the Claude Code plugin's server. <see cref="Command"/> is the full path of the eplanmcp.exe that runs client-config:
/// the installed <c>%USERPROFILE%\EplanMcp\app\eplanmcp.exe</c> (ADR-0016), or a development build's own.
/// </summary>
internal sealed record LocalServer(string Command)
{
    public const string Name = "eplan";

    public static IReadOnlyList<string> Arguments { get; } = ["serve"];
}
