using System.Globalization;
using System.Text;
using EplanMcp.Bridge.Core.Diagnostics;

namespace EplanMcp.Bridge.Eplan.Hosting;

/// <summary>
/// Appends to <c>&lt;data root&gt;\logs\bridge-yyyyMMdd.log</c> (UTF-8), or with another file name prefix such as the
/// offline host's <c>bridge-offline-</c>. Never throws: logging must not be able to break an EPLAN callback. Thread-safe.
/// </summary>
internal sealed class FileBridgeLog(string directory, string fileNamePrefix = "bridge-") : IBridgeLog
{
    private readonly object _gate = new();

    public void Write(BridgeLogLevel level, string message, Exception? exception = null)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(level.ToString()).Append("] ")
                .Append(message);
            if (exception is not null)
            {
                line.Append(Environment.NewLine).Append(exception);
            }

            line.Append(Environment.NewLine);
            string path = Path.Combine(directory, fileNamePrefix + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(path, line.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Nowhere left to report to; the Bridge keeps working without a log.
        }
    }
}
