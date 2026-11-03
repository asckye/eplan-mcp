using System.Globalization;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Eplan.Hosting;

namespace EplanMcp.Bridge.Core.Tests.Hosting;

[Trait("Size", "Medium")]
public sealed class FileBridgeLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EplanMcp-log-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_RedactsMessageAndExceptionBeforeAppending()
    {
        var log = new FileBridgeLog(_directory);

        log.Write(BridgeLogLevel.Error, "password=abc123 Authorization: Bearer abc.def https://user:pw@host/", new InvalidOperationException("token=exception-secret"));

        string text = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
        Assert.DoesNotContain("abc123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def", text, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pw", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-secret", text, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: token=[REDACTED]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_OrdinaryMessageAndException_PreservesExistingFormat()
    {
        var log = new FileBridgeLog(_directory, "bridge-offline-");
        var exception = new InvalidOperationException("ordinary failure");

        log.Write(BridgeLogLevel.Error, "Bridge connected.", exception);

        string path = Assert.Single(Directory.GetFiles(_directory));
        Assert.StartsWith("bridge-offline-", Path.GetFileName(path), StringComparison.Ordinal);
        string text = File.ReadAllText(path);
        Assert.True(DateTime.TryParseExact(text[..23], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        Assert.Equal(" [Error] Bridge connected." + Environment.NewLine + exception + Environment.NewLine, text[23..]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
