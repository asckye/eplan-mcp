using System.Globalization;
using EplanMcp.Contracts.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace EplanMcp.Host.Logging;

/// <summary>SimpleConsole's uncolored, single-line layout with credentials removed at the output boundary.</summary>
internal sealed class RedactingConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    internal const string FormatterName = "redacting";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
        {
            return;
        }

        string level = logEntry.LogLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logEntry)),
        };
        textWriter.Write(level);
        textWriter.Write(": ");
        textWriter.Write(logEntry.Category);
        textWriter.Write('[');
        textWriter.Write(logEntry.EventId.Id.ToString(CultureInfo.InvariantCulture));
        textWriter.Write(']');
        if (!string.IsNullOrEmpty(message))
        {
            textWriter.Write(' ');
            WriteText(textWriter, message);
        }
        if (logEntry.Exception is not null)
        {
            textWriter.Write(' ');
            WriteText(textWriter, logEntry.Exception.ToString());
        }
        textWriter.Write(Environment.NewLine);
    }

    private static void WriteText(TextWriter writer, string text)
    {
        writer.Write(SecretRedactor.Redact(text.Replace(Environment.NewLine, " ", StringComparison.Ordinal)));
    }
}
