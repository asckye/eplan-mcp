using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;
using EplanMcp.Tests;

namespace EplanMcp.Host.Tests.Audit;

/// <summary>The tests' audit reader, beside a Host that is appending to the day's file.</summary>
[Trait("Size", "Medium")]
public sealed class AuditFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eplanmcp-audit-files-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Records_WhileTheHostAppends_AreTheCompleteOnes()
    {
        await new AuditLog(_directory, TimeProvider.System).AppendAsync(new JsonObject { ["kind"] = "execution_started" }, TestContext.Current.CancellationToken);
        string file = Directory.EnumerateFiles(_directory, AuditLog.FilePattern).Single();

        // Held the way AuditLog.AppendAsync holds it, halfway through the next record.
        using (var appending = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            appending.Write(Encoding.UTF8.GetBytes("{\"time\":\"2026-"));
            appending.Flush();

            Assert.Throws<IOException>(() => File.ReadAllLines(file)); // what the tests read with before
            Assert.Equal(["execution_started"], AuditFiles.Records(_directory).Select(r => r["kind"]!.GetValue<string>()));
        }
    }

    [Fact]
    public void Records_WithoutAnAuditDirectory_AreNone() =>
        Assert.Empty(AuditFiles.Records(Path.Combine(_directory, "audit")));
}
