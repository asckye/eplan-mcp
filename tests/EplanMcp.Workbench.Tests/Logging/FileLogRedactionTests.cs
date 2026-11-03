using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Logging;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Workbench.Tests.Logging;

[Trait("Size", "Small")]
public sealed class FileLogRedactionTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-workbench-redaction-");
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void Credentials_InFormattedMessageAndException_AreRedactedBeforeWriting()
    {
        using var log = new FileLog(_root.FullName, _clock);

        log.Log(LogLevel.Error, default, "password=abc123 Authorization: Bearer abc.def https://user:pw@host/",
            new IOException("token=exception-secret"), (message, _) => message);

        string text = File.ReadAllText(Path.Combine(_root.FullName, "workbench-20261021.log"));
        Assert.Equal("2026-10-21T08:00:00.000Z Error password=[REDACTED] Authorization: Bearer [REDACTED] https://[REDACTED]@host/ | IOException: token=[REDACTED]" + Environment.NewLine, text);
        Assert.DoesNotContain("abc123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def", text, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pw", text, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleLineNormalization_DoesNotExposeCredentialValues()
    {
        using var log = new FileLog(_root.FullName, _clock);

        log.Log(LogLevel.Information, default, "password=\r\nline-secret", null, (text, _) => text);

        string output = File.ReadAllText(Path.Combine(_root.FullName, "workbench-20261021.log"));
        Assert.DoesNotContain("line-secret", output, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "2026-10-21T08:00:00.000Z Information Question r1 arrived")]
    [InlineData(true, "2026-10-21T08:00:00.000Z Information Question r1 arrived | IOException: pipe broken")]
    public void OrdinaryMessageAndException_KeepTheirExactOutput(bool withException, string expected)
    {
        using var log = new FileLog(_root.FullName, _clock);

        log.Log(LogLevel.Information, default, "Question r1 arrived",
            withException ? new IOException("pipe\r\nbroken") : null, (message, _) => message);

        Assert.Equal(expected + Environment.NewLine, File.ReadAllText(Path.Combine(_root.FullName, "workbench-20261021.log")));
    }
}
