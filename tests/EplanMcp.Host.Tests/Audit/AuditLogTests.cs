using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;

namespace EplanMcp.Host.Tests.Audit;

/// <summary>The audit hash chain on disk (architecture 8.8, SEC-09, Q7, Q9).</summary>
[Trait("Size", "Medium")]
public sealed class AuditLogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-audit-").FullName;
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    private string AuditDirectory => Path.Combine(_root, "audit");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Append_ChainsEveryRecordToTheOneBefore()
    {
        var entry = new JsonObject { ["kind"] = "execution_started", ["tool"] = "eplan_page_create", ["project"] = "D:\\Scratch\\线路1" };

        string first = await Append(entry);
        string second = await Append(new JsonObject { ["kind"] = "result", ["execution"] = "committed" });

        string[] lines = File.ReadAllLines(Path.Combine(AuditDirectory, "2026-10-20.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.Equal(("", first), (Field(lines[0], "prev"), Field(lines[0], "hash")));
        Assert.Equal((first, second), (Field(lines[1], "prev"), Field(lines[1], "hash")));
        Assert.Contains("线路1", lines[0], StringComparison.Ordinal);
        Assert.Equal("2026-10-20T08:00:00.0000000+00:00", Field(lines[0], "time"));
        Assert.False(entry.ContainsKey("hash"));
        Assert.Equal((2, null), AuditVerifier.Verify(AuditDirectory));
    }

    [Fact]
    public async Task Verify_AChangedRecord_BreaksTheChainThere()
    {
        await AppendThree();
        Rewrite(lines => lines[1] = lines[1].Replace("\"n\":2", "\"n\":20", StringComparison.Ordinal));

        Assert.Equal((1, new AuditBreak("2026-10-20.jsonl", 2, AuditProblem.Changed)), AuditVerifier.Verify(AuditDirectory));
    }

    [Fact]
    public async Task Verify_ARemovedRecord_BreaksTheChainAtTheNextOne()
    {
        await AppendThree();
        Rewrite(lines => lines.RemoveAt(1));

        Assert.Equal((1, new AuditBreak("2026-10-20.jsonl", 2, AuditProblem.Missing)), AuditVerifier.Verify(AuditDirectory));
    }

    [Fact]
    public async Task Verify_ALineThatIsNoRecord_BreaksTheChain()
    {
        await AppendThree();
        Rewrite(lines => lines.Insert(2, "{\"kind\":\"forged\"}"));

        Assert.Equal((2, new AuditBreak("2026-10-20.jsonl", 3, AuditProblem.NotARecord)), AuditVerifier.Verify(AuditDirectory));
    }

    [Fact]
    public async Task Verify_RecordsRemovedFromTheEnd_LeaveNoTrace()
    {
        // A known limit of a hash chain alone: nothing comes after the removed records to notice.
        await AppendThree();
        Rewrite(lines => lines.RemoveAt(2));

        Assert.Equal((2, null), AuditVerifier.Verify(AuditDirectory));
    }

    [Fact]
    public async Task Append_TheChainContinuesIntoTheNextDay()
    {
        string first = await Append(new JsonObject { ["n"] = 1 });
        _clock.Now = _clock.Now.AddDays(1);
        await Append(new JsonObject { ["n"] = 2 });

        Assert.Equal(first, Field(File.ReadAllLines(Path.Combine(AuditDirectory, "2026-10-21.jsonl"))[0], "prev"));
        Assert.Equal((2, null), AuditVerifier.Verify(AuditDirectory));

        File.Delete(Path.Combine(AuditDirectory, "2026-10-20.jsonl"));
        Assert.Equal((0, new AuditBreak("2026-10-21.jsonl", 1, AuditProblem.Missing)), AuditVerifier.Verify(AuditDirectory));
    }

    [Fact]
    public async Task Append_SeveralHostsAtOnce_KeepOneChain()
    {
        // Two logs on one directory stand for two Host processes; the lock file makes them take turns.
        var other = new AuditLog(AuditDirectory, _clock);
        await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            Task.Run(() => (i % 2 == 0 ? Log() : other).AppendAsync(new JsonObject { ["n"] = i }, TestContext.Current.CancellationToken))));

        Assert.Equal((40, null), AuditVerifier.Verify(AuditDirectory));
    }

    [Fact]
    public async Task Append_WhenTheLogCannotBeWritten_SaysSo()
    {
        File.WriteAllText(AuditDirectory, "a file where the audit directory should be");

        await Assert.ThrowsAsync<AuditUnavailableException>(() => Append(new JsonObject { ["n"] = 1 }));
    }

    [Fact]
    public async Task Command_ReportsTheChainAndWhereItBreaks()
    {
        var locations = new HostDataLocations(_root);
        using var empty = new StringWriter();
        Assert.Equal(0, AuditCommand.Verify(empty, CultureInfo.GetCultureInfo("en-US"), locations));
        Assert.StartsWith("No audit records", empty.ToString(), StringComparison.Ordinal);

        await AppendThree();
        using var intact = new StringWriter();
        Assert.Equal(0, AuditCommand.Verify(intact, CultureInfo.GetCultureInfo("en-US"), locations));
        Assert.Contains("intact: 3 records", intact.ToString(), StringComparison.Ordinal);

        Rewrite(lines => lines.RemoveAt(1));
        using var broken = new StringWriter();
        Assert.Equal(1, AuditCommand.Verify(broken, CultureInfo.GetCultureInfo("zh-CN"), locations));
        Assert.Contains("2026-10-20.jsonl:2", broken.ToString(), StringComparison.Ordinal);
        Assert.Contains("被删除", broken.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Doctor_ReportsWhetherTheLogCanBeWritten()
    {
        var locations = new HostDataLocations(_root);
        Assert.Equal(EplanMcp.Host.Doctor.DoctorStatus.Info, EplanMcp.Host.Doctor.DoctorChecks.AuditDirectory(locations).Status);

        await AppendThree();

        Assert.Equal(EplanMcp.Host.Doctor.DoctorStatus.Ok, EplanMcp.Host.Doctor.DoctorChecks.AuditDirectory(locations).Status);
    }

    private AuditLog Log() => new(AuditDirectory, _clock);

    private Task<string> Append(JsonObject entry) => Log().AppendAsync(entry, TestContext.Current.CancellationToken);

    private async Task AppendThree()
    {
        for (int n = 1; n <= 3; n++)
        {
            await Append(new JsonObject { ["n"] = n });
        }
    }

    private void Rewrite(Action<List<string>> change)
    {
        string file = Path.Combine(AuditDirectory, "2026-10-20.jsonl");
        List<string> lines = [.. File.ReadAllLines(file)];
        change(lines);
        File.WriteAllLines(file, lines);
    }

    private static string Field(string line, string name) => JsonNode.Parse(line)![name]!.GetValue<string>();

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
