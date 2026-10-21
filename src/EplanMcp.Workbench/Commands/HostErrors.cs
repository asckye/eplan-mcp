using System.Globalization;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Commands;

/// <summary>A Host command's failure as the pages show it: what happened and what to do, then the details (ERR-01, ERR-02).</summary>
internal static class HostErrors
{
    /// <summary>The text of the failure in the current language, never the exception's own message.</summary>
    public static string Text(HostCommandException error) => Loc.Current["Host." + error.Failure];

    /// <summary>
    /// The command line, the exit code, what went wrong and the end of what the Host wrote to standard error, for the
    /// details below the text (Code level).
    /// </summary>
    public static string Details(HostCommandException error) => string.Join(
        Environment.NewLine,
        new[]
        {
            error.CommandLine,
            error.ExitCode?.ToString(CultureInfo.InvariantCulture) is { } code ? Loc.Current.T("Host.ExitCode", code) : null,
            error.Message,
            error.Errors.Length > 0 ? error.Errors : null,
        }.OfType<string>());
}
