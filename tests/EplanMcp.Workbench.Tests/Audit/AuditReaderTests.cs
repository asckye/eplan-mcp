using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Audit;

namespace EplanMcp.Workbench.Tests.Audit;

/// <summary>
/// The Workbench reads the audit log as the Host does (M5 design 4.4 item 2): the records the Host's own AuditLog
/// wrote, whole and in order; never what follows the last "\n"; while a Host appends; and without writing anything.
/// </summary>
[Trait("Size", "Medium")]
public sealed class AuditReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eplanmcp-workbench-audit-").FullName;
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.Zero));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task TheHostsRecords_AreReadWhole_OldestFirst_AcrossTheDaysFiles()
    {
        var log = new AuditLog(_directory, _clock);
        await log.AppendAsync(new JsonObject { ["kind"] = "approval", ["tool"] = "eplan_page_create", ["project"] = @"D:\Projects\线路1", ["decision"] = "granted", ["changes"] = new JsonArray("新建页 =A1&EFS1/5") }, Token);
        _clock.Advance(TimeSpan.FromHours(1));
        await log.AppendAsync(new JsonObject { ["kind"] = "execution_result", ["tool"] = "eplan_page_create", ["execution"] = "committed", ["recovered"] = false }, Token);
        var progress = new List<double>();

        AuditLogContents contents = AuditReader.Read(_directory, new SyncProgress(progress), Token);

        Assert.Equal([("2026-10-20.jsonl", 1), ("2026-10-21.jsonl", 1)], contents.Records.Select(r => (r.File, r.Line)));
        AuditRecord approval = contents.Records[0];
        Assert.Equal(["time", "kind", "tool", "project", "decision", "changes", "prev", "hash"], approval.Fields.Select(f => f.Key));
        Assert.Equal(("approval", @"D:\Projects\线路1", "granted", "[\"新建页 =A1&EFS1/5\"]"), (approval.Kind, approval.Project, approval.Decision, approval.Field("changes")));
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.Zero), approval.Time);
        Assert.Equal(("committed", "false"), (contents.Records[1].Execution, contents.Records[1].Field("recovered")));
        Assert.Equal([0.5, 1.0], progress);
        Assert.Equal(0, contents.NotRecords);
    }

    [Fact]
    public void AYearOfFiles_IsReportedAtMostAHundredTimes()
    {
        for (int day = 0; day < 365; day++)
        {
            File.WriteAllText(Path.Combine(_directory, new DateOnly(2025, 1, 1).AddDays(day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl"), "{\"kind\":\"approval\"}\n");
        }

        var progress = new List<double>();
        AuditLogContents contents = AuditReader.Read(_directory, new SyncProgress(progress), Token);

        Assert.Equal(365, contents.Records.Count);
        Assert.InRange(progress.Count, 1, 100);
        Assert.Equal(1.0, progress[^1]);
    }

    [Fact]
    public void WhatFollowsTheLastLineBreak_IsLeftOut_AndLinesThatAreNotRecords_AreCounted()
    {
        File.WriteAllText(Path.Combine(_directory, "2026-10-20.jsonl"), "{\"kind\":\"approval\"}\nnot json\n[1,2]\n{\"kind\":\"a\",\"kind\":\"b\"}\n\n{\"kind\":\"execution_res", Encoding.UTF8);

        AuditLogContents contents = AuditReader.Read(_directory, null, Token);

        Assert.Equal("approval", Assert.Single(contents.Records).Kind);
        Assert.Equal(3, contents.NotRecords);
    }

    [Fact]
    public void AFileAHostIsAppendingTo_IsRead_AndTheHostCanGoOnAppending()
    {
        string file = Path.Combine(_directory, "2026-10-20.jsonl");
        using var appender = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read); // as AuditLog appends
        appender.Write(Encoding.UTF8.GetBytes("{\"kind\":\"approval\"}\n{\"kind\":\"exec"));
        appender.Flush();

        AuditLogContents contents = AuditReader.Read(_directory, null, Token);
        appender.Write(Encoding.UTF8.GetBytes("ution_result\"}\n"));
        appender.Flush();

        Assert.Equal("approval", Assert.Single(contents.Records).Kind);
        Assert.Equal(2, AuditReader.Read(_directory, null, Token).Records.Count);
    }

    [Fact]
    public async Task Reading_WritesNothing_NotEvenTheLockFile()
    {
        await new AuditLog(_directory, _clock).AppendAsync(new JsonObject { ["kind"] = "approval" }, Token);
        File.Delete(Path.Combine(_directory, ".lock"));
        string file = Path.Combine(_directory, "2026-10-20.jsonl");
        (byte[] bytes, DateTime written) = (File.ReadAllBytes(file), File.GetLastWriteTimeUtc(file));

        AuditReader.Read(_directory, null, Token);

        Assert.Equal([file], Directory.GetFiles(_directory));
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void AFolderThatIsNotThere_HasNoRecords_AndOtherFilesAreNotRead()
    {
        File.WriteAllText(Path.Combine(_directory, "notes.jsonl"), "{\"kind\":\"approval\"}\n");

        Assert.Empty(AuditReader.Read(Path.Combine(_directory, "missing"), null, Token).Records);
        Assert.Empty(AuditReader.Read(_directory, null, Token).Records);
    }

    /// <summary>Progress reported on the reading thread itself, so the test sees every step in order.</summary>
    private sealed class SyncProgress(List<double> steps) : IProgress<double>
    {
        public void Report(double value) => steps.Add(value);
    }
}
