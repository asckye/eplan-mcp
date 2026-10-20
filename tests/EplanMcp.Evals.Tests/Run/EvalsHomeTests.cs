using EplanMcp.Evals.Run;

namespace EplanMcp.Evals.Tests.Run;

/// <summary>The data root of the model's eplanmcp, in a temporary directory.</summary>
[Trait("Size", "Medium")]
public sealed class EvalsHomeTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-evals-home-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Write_AllowsWritesOnlyInTheCopiesFolder()
    {
        string home = Path.Combine(_directory.FullName, "eplanmcp");

        EvalsHome.Write(home, @"C:\Users\me\EplanMcp\scratch\evals\r1\copies");

        Assert.Equal(
            """{"scratchRoots":["C:\\Users\\me\\EplanMcp\\scratch\\evals\\r1\\copies"],"writesOutsideScratch":"deny"}""",
            File.ReadAllText(Path.Combine(home, "config.json")));
    }

    [Fact]
    public void AuditedWrites_CountsTheResultRecordsOfEveryDay()
    {
        string home = _directory.FullName;
        Assert.Equal(0, EvalsHome.AuditedWrites(home));

        Directory.CreateDirectory(Path.Combine(home, "audit"));
        File.WriteAllLines(Path.Combine(home, "audit", "2026-10-20.jsonl"), ["""{"kind":"execution_started"}""", """{"kind":"execution_result","execution":"committed"}""", "not json", "[1]"]);
        File.WriteAllLines(Path.Combine(home, "audit", "2026-10-21.jsonl"), ["""{"kind":"execution_result","execution":"rolled_back"}"""]);

        Assert.Equal(2, EvalsHome.AuditedWrites(home));
    }

    [Fact]
    public void AuditedWrites_ReadsALogThatEplanmcpIsStillAppendingTo()
    {
        string audit = Directory.CreateDirectory(Path.Combine(_directory.FullName, "audit")).FullName;

        // As AuditLog.AppendAsync opens the day's file.
        using (var writer = new FileStream(Path.Combine(audit, "2026-10-20.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            writer.Write("""{"kind":"execution_result"}"""u8.ToArray().Append((byte)'\n').ToArray());
            writer.Flush();

            Assert.Equal(1, EvalsHome.AuditedWrites(_directory.FullName));
        }
    }
}
