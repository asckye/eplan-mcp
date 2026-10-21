using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Logging;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Workbench.Tests.Logging;

/// <summary>The Workbench's log file: one line per entry, by day, and never a reason to fail.</summary>
[Trait("Size", "Medium")]
public sealed class FileLogTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-workbench-log-");
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => _root.Delete(recursive: true);

    private static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null) =>
        logger.Log(level, default, message, exception, (text, _) => text);

    [Fact]
    public void Entries_AreSingleLinesInTheDaysFile_FromInformationUp()
    {
        string logs = Path.Combine(_root.FullName, "logs");
        using var log = new FileLog(logs, _clock);
        ILogger logger = log.CreateLogger("test");

        Write(logger, LogLevel.Information, "Question r1 arrived");
        Write(logger, LogLevel.Warning, "Connection closed", new IOException("pipe\r\nbroken"));
        Write(logger, LogLevel.Debug, "not written");

        string[] lines = File.ReadAllLines(Path.Combine(logs, "workbench-20261021.log"));
        Assert.Equal(["2026-10-21T08:00:00.000Z Information Question r1 arrived", "2026-10-21T08:00:00.000Z Warning Connection closed | IOException: pipe broken"], lines);
    }

    [Fact]
    public void ALogThatCannotBeWritten_FailsNothing()
    {
        string blocked = Path.Combine(_root.FullName, "file");
        File.WriteAllText(blocked, "a file where the logs folder should be");
        using var log = new FileLog(blocked, _clock);

        Write(log.CreateLogger("test"), LogLevel.Error, "still running");

        Assert.Equal("a file where the logs folder should be", File.ReadAllText(blocked));
    }
}
