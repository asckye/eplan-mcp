using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// Writes a crashed Host left without an outcome (ADR-0010 section 2). The test process stands in for the running
/// EPLAN; a record whose Host start time does not match any process stands for the crashed Host.
/// </summary>
[Trait("Size", "Medium")]
public sealed class DanglingWritesTests : IDisposable
{
    private static readonly string _now = SuspensionStore.StartOf(Environment.ProcessId)!.Value.ToString("O", CultureInfo.InvariantCulture);
    private static readonly string _longAgo = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-dangling-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private readonly AuditLog _audit;
    private readonly string _project;

    public DanglingWritesTests()
    {
        _audit = new AuditLog(_locations.Audit, TimeProvider.System);
        _project = Path.Combine(_locations.DefaultScratch, "Line1");
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task ACommitTheBridgeRecorded_IsRecordedLate()
    {
        await Started("r1", hostStart: _longAgo, eplanStart: _now);
        _bridge.Outcomes["r1"] = new OutcomeGetData { Status = OutcomeStatus.Committed, Operation = "page.create.execute" };

        await Settle();

        JsonObject result = Records().Single(r => Kind(r) == "execution_result");
        Assert.Equal(("committed", true), (result["execution"]!.GetValue<string>(), result["late"]!.GetValue<bool>()));
        Assert.Empty(Suspensions().All());
        Assert.Empty(_audit.Dangling());
    }

    [Fact]
    public async Task PlanStepCorrelation_SurvivesOutcomeSettlementAndSuspension()
    {
        await _audit.AppendAsync(new JsonObject
        {
            ["kind"] = "execution_started",
            ["requestId"] = "plan-step-request",
            ["tool"] = "eplan_plan_apply",
            ["project"] = _project,
            ["planId"] = "0123456789abcdef0123456789abcdef",
            ["step"] = 2,
            ["host"] = new JsonObject { ["processId"] = Environment.ProcessId, ["start"] = _longAgo },
            ["eplan"] = new JsonObject { ["processId"] = Environment.ProcessId, ["start"] = _now },
        }, TestContext.Current.CancellationToken);

        await Settle();

        JsonObject[] settled = [.. Records().Where(r => Kind(r) is "execution_result" or "writes_suspended")];
        Assert.Equal(2, settled.Length);
        Assert.All(settled, r =>
        {
            Assert.Equal("0123456789abcdef0123456789abcdef", r["planId"]!.GetValue<string>());
            Assert.Equal(2, r["step"]!.GetValue<int>());
        });
    }

    [Fact]
    public async Task AnOutcomeNobodyCanTell_SuspendsTheProject()
    {
        await Started("r1", hostStart: _longAgo, eplanStart: _now); // the Bridge has no answer

        await Settle();

        Assert.Equal(["execution_started", "execution_result", "writes_suspended"], Records().Select(Kind));
        Assert.Equal("unknown", Records()[1]["execution"]!.GetValue<string>());
        Assert.Contains(Suspensions().All(), s => s.Project is not null && s.RequestId == "r1");
    }

    [Fact]
    public async Task AMasterDataWriteNobodyCanTell_SuspendsTheDatabase_NotAProject()
    {
        // ADR-0019: the record names the database and no project; the database's suspension is keyed by its path.
        await _audit.AppendAsync(
            new JsonObject
            {
                ["kind"] = "execution_started",
                ["requestId"] = "r1",
                ["tool"] = "eplan_part_import",
                ["project"] = string.Empty,
                ["masterData"] = new JsonObject { ["kind"] = MasterDataKinds.Parts, ["database"] = @"$(MD_PARTS)\EPLAN_parts.alk", ["path"] = @"C:\Parts\EPLAN_parts.alk", ["server"] = false },
                ["eplan"] = new JsonObject { ["version"] = "2026.0.3", ["processId"] = Environment.ProcessId, ["start"] = _now },
                ["host"] = new JsonObject { ["processId"] = Environment.ProcessId, ["start"] = _longAgo },
                ["changes"] = new JsonArray("add 3 parts"),
            },
            TestContext.Current.CancellationToken);

        await Settle();

        IReadOnlyList<Suspension> all = Suspensions().All();
        Assert.Contains(all, s => s.Database == @"$(MD_PARTS)\EPLAN_parts.alk" && s.RequestId == "r1");
        Assert.DoesNotContain(all, s => s.Project is not null);
        Assert.Equal(@"$(MD_PARTS)\EPLAN_parts.alk", Records()[^1]["masterData"]!["database"]!.GetValue<string>());
    }

    [Fact]
    public async Task AWriteStillRunning_IsSuspendedAsSuch()
    {
        await Started("r1", hostStart: _longAgo, eplanStart: _now);
        _bridge.Outcomes["r1"] = new OutcomeGetData { Status = OutcomeStatus.Running, Operation = "page.create.execute" };

        await Settle();

        Assert.All(Suspensions().All(), s => Assert.Equal(SuspensionReason.StillRunning, s.Reason));
    }

    [Fact]
    public async Task WhenItsEplanHasExited_TheBridgeIsNotAskedAndTheProjectIsSuspended()
    {
        await Started("r1", hostStart: _longAgo, eplanStart: _longAgo);
        _bridge.Outcomes["r1"] = new OutcomeGetData { Status = OutcomeStatus.Committed }; // another EPLAN's answer must not count

        await Settle();

        Assert.Equal("unknown", Records()[1]["execution"]!.GetValue<string>());
        Assert.Contains(Suspensions().All(), s => s.Project is not null);
    }

    [Fact]
    public async Task AWriteOfAHostStillRunning_IsLeftAlone()
    {
        await Started("r1", hostStart: _now, eplanStart: _now);

        await Settle();

        Assert.Single(Records());
        Assert.Empty(Suspensions().All());
    }

    [Fact]
    public async Task Settling_HappensOncePerHost()
    {
        DanglingWrites dangling = New();
        await dangling.SettleOnceAsync(TestContext.Current.CancellationToken);
        await Started("r1", hostStart: _longAgo, eplanStart: _now);

        await dangling.SettleOnceAsync(TestContext.Current.CancellationToken);

        Assert.Single(Records());
    }

    private DanglingWrites New() => new(_bridge, Suspensions(), _audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance);

    private Task Settle() => New().SettleOnceAsync(TestContext.Current.CancellationToken);

    private SuspensionStore Suspensions() => SuspensionStore.For(_locations);

    private Task<string> Started(string requestId, string hostStart, string eplanStart) => _audit.AppendAsync(
        new JsonObject
        {
            ["kind"] = "execution_started",
            ["requestId"] = requestId,
            ["tool"] = "eplan_page_create",
            ["project"] = _project,
            ["eplan"] = new JsonObject { ["version"] = "2026.0.3", ["processId"] = Environment.ProcessId, ["start"] = eplanStart },
            ["host"] = new JsonObject { ["processId"] = Environment.ProcessId, ["start"] = hostStart },
            ["changes"] = new JsonArray("create page =A1&EFS1/5"),
        },
        TestContext.Current.CancellationToken);

    private List<JsonObject> Records() =>
        [.. Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];

    private static string Kind(JsonObject record) => record["kind"]!.GetValue<string>();
}
