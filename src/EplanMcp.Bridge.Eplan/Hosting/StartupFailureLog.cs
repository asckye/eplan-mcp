using System.Globalization;
using System.Text;

namespace EplanMcp.Bridge.Eplan.Hosting;

/// <summary>
/// Last-resort report when starting or stopping the Bridge failed, possibly because Bridge.Core or Contracts could
/// not be loaded. Deliberately separate from <see cref="BridgeHost"/> and BCL-only, so that reporting cannot fail
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
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + exception + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Nowhere left to report to; EPLAN keeps running without the Bridge.
        }
    }
}
