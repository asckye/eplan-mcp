using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Workbench.Logging;

/// <summary>
/// The Workbench's log: <c>logs\workbench-yyyyMMdd.log</c> in the data root (M5 design 4.1), one UTF-8 line per entry,
/// Information and above. One Workbench runs per data root, so it is the only writer. Writing never fails the caller.
/// Callers log request IDs, tools and errors, never project data such as the change list (APR-08, SEC-10).
/// </summary>
internal sealed class FileLog(string folder, TimeProvider clock) : ILoggerProvider, ILogger
{
    private readonly Lock _lock = new();

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        string message = formatter(state, exception) + (exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}");
        string line = string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-ddTHH:mm:ss.fffZ} {logLevel} {message.ReplaceLineEndings(" ")}{Environment.NewLine}");
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"workbench-{now:yyyyMMdd}.log")), line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written must not take the approval channel down with it.
        }
    }

    public void Dispose()
    {
    }
}
