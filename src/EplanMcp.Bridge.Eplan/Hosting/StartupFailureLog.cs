using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using EplanMcp.Contracts.Security;

namespace EplanMcp.Bridge.Eplan.Hosting;

/// <summary>
/// Last-resort report when starting or stopping the Bridge failed, possibly because Bridge.Core or Contracts could
/// not be loaded. Deliberately separate from <c>BridgeHost</c> and BCL-only, so that reporting cannot fail
/// for the same reason. Never throws.
/// </summary>
internal static class StartupFailureLog
{
    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DataLocations.Logs);
            File.AppendAllText(
                Path.Combine(DataLocations.Logs, "bridge-startup-error.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + Describe(exception) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Nowhere left to report to; EPLAN keeps running without the Bridge.
        }
    }

    internal static string Describe(Exception exception, Func<string, string>? redact = null)
    {
        try
        {
            return (redact ?? Redact)(exception.ToString());
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException or MissingMemberException or TypeInitializationException)
        {
            // Contracts may be unavailable during startup; never fall back to uninspected messages.
            var text = new StringBuilder("redaction unavailable; messages omitted");
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                text.AppendLine().Append(current.GetType().FullName)
                    .Append(" HResult=0x").Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture))
                    .AppendLine().Append(current.StackTrace);
            }

            return text.ToString();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Redact(string text) => SecretRedactor.Redact(text);
}
