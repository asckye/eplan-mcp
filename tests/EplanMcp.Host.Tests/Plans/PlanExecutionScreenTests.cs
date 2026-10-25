using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Plans;

public sealed partial class PlanExecutionTests
{
    private Action<string>? _coverageFault;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version2_OutsideNameChange_ReenumeratesOnceAndStopsWithoutResending(bool between)
    {
        ConfigureLocal();
        await Prepare(local: true);
        Func<object?, object> names = _bridge.Handlers[OperationNames.PlanNames];
        int scans = 0;
        _bridge.Handlers[OperationNames.PlanNames] = args =>
        {
            scans++;
            if (scans == (between ? 3 : 2))
            {
                _current = PlanVerificationTestData.Copy(_current, pages => pages.Add(FarPage("+OUT/1", 1000)));
            }
            return names(args);
        };
        PlanRecord record = await _service.ExecuteAsync(_server, _prepared!, null, Token);
        PlanResult result = record.State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(ErrorReason.PlanStale, result.Error!.Reason);
        Assert.Single(_bridge.Writes);
        Assert.Equal(between ? 4 : 3, scans);
        JsonElement change = Assert.Single(result.Diff!.Value.EnumerateArray(), d => d.GetProperty("property").GetString() == "names"
            && d.GetProperty("class").GetString() != "planned_missing");
        Assert.Equal(between ? "external_between_steps" : "outside_scope", change.GetProperty("class").GetString());
        Assert.Equal(between ? "external" : "unknown", change.GetProperty("attribution").GetString());
        Assert.Equal("+OUT/1", change.GetProperty("locator").GetProperty("name").GetString());
        Assert.All(result.Steps.Skip(1), s => Assert.Equal(ExecutionState.NotStarted, s.Execution));
        Assert.Equal(result.Execution, (await _store.ReadAsync(record.Definition.PlanId, Token))!.State.Result!.Execution);
    }

    [Fact]
    public async Task Version2_NameRescanIncomplete_PreservesInitialAlertAndCommit()
    {
        ConfigureLocal();
        await Prepare(local: true);
        Func<object?, object> names = _bridge.Handlers[OperationNames.PlanNames];
        int scans = 0;
        _bridge.Handlers[OperationNames.PlanNames] = args =>
        {
            scans++;
            if (scans == 2)
            {
                _current = PlanVerificationTestData.Copy(_current, pages => pages.Add(FarPage("+OUT/1", 1000)));
            }
            var data = (PlanNamesData)names(args);
            data.Complete = scans != 3;
            return data;
        };
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Error!.Reason);
        Assert.Equal("failed", result.Verification);
        Assert.Single(_bridge.Writes);
        Assert.Equal(3, scans);
        Assert.Contains(result.Diff!.Value.EnumerateArray(), d => d.GetProperty("class").GetString() == "outside_scope");
    }

    [Fact]
    public async Task Version2_FarDescription_NameScreenPassesAndFullCoverageIsStoredAndPaged()
    {
        ConfigureLocal();
        const string far = "+FAR/1";
        PlanOccupiedName[] farNames = [.. Enumerable.Range(1, 10_000).Select(i => new PlanOccupiedName("page", "+FAR/" + i))];
        _current = PlanVerificationTestData.Copy(_current, pages => pages.Add(FarPage(far, 1000)));
        Func<object?, object> names = _bridge.Handlers[OperationNames.PlanNames];
        _bridge.Handlers[OperationNames.PlanNames] = args =>
        {
            var data = (PlanNamesData)names(args);
            PlanOccupiedName[] all = [.. data.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name)).Concat(farNames).Distinct()];
            data.Names = [.. all.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })];
            data.NamesHash = PlanSnapshotHashes.Names(_current.Project.Path, all);
            return data;
        };
        await Prepare(local: true, baselineNames: [.. _baseline.Names, .. farNames]);
        _after = count =>
        {
            if (count == 1)
            {
                _current = PlanVerificationTestData.Copy(_current,
                    pages => PlanVerificationTestData.Description(pages.Single(p => p.Name == far).Objects[0], "Unobserved change"));
            }
        };
        _bridge.Calls.Clear();
        PlanRecord record = await _service.ExecuteAsync(_server, _prepared!, null, Token);
        PlanResult result = record.State.Result!;

        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal("passed", result.Verification);
        Assert.Equal(6, _bridge.Calls.Count(c => c.Operation == OperationNames.PlanNames));
        Assert.DoesNotContain(result.Diff!.Value.EnumerateArray(), d => d.GetProperty("locator").TryGetProperty("name", out JsonElement n) && n.GetString() == far);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(record.State, PlanJson.Options).Length < PlanCapacity.MiB);
        Assert.True(result.Coverage!.Value.GetRawText().Length < 16_384);
        Assert.All(result.Coverage.Value.GetProperty("entries").EnumerateArray(), c =>
            Assert.InRange(c.GetProperty("unobservedPages").GetArrayLength(), 0, 8));
        var full = await PlanCoverage.ReadAsync(_store, record, Token);
        Assert.Equal(6, full.Length);
        Assert.All(full, c => Assert.Empty(farNames.Select(n => n.Name).Except(c.UnobservedPages, StringComparer.Ordinal)));
        Assert.All(full, c =>
        {
            Assert.Contains("properties.outsideScope", c.PropertyGaps);
            Assert.Contains("functions.sameNameInstancesOutsideClosure", c.PropertyGaps);
            Assert.Contains("objects.withoutPage.unreadProperties", c.PropertyGaps);
            Assert.Contains("graphics", c.PropertyGaps);
            Assert.Contains("texts", c.PropertyGaps);
            Assert.Contains("placeholders", c.PropertyGaps);
            Assert.Contains("connections.3d", c.PropertyGaps);
        });
        JsonElement references = result.Coverage.Value.GetProperty("evidence");
        string hash = references.GetProperty("1:write:coverage").GetString()!;
        PlanVerificationCoverage stored = JsonSerializer.Deserialize<PlanVerificationCoverage>(
            await _store.ReadEvidenceAsync(record.Definition.PlanId, hash, Token), PlanJson.Options)!;
        Assert.Contains("+FAR/10000", stored.UnobservedPages);

        var entries = PlanEntries.Build(record, true, full);
        var fragments = entries.Where(e => e["key"]!.GetValue<string>() == "coverage:write" && e["index"]!.GetValue<int>() == 1)
            .Select(e => e["value"]!.GetValue<string>());
        PlanVerificationCoverage paged = JsonSerializer.Deserialize<PlanVerificationCoverage>(string.Concat(fragments), PlanJson.Options)!;
        Assert.Equal(stored.UnobservedPages, paged.UnobservedPages);
        var call = await PlanTools.GetAsync(_store, _trust, _tasks, _server, record.Definition.PlanId, "detailed", 1, null, Token);
        Assert.False(call.IsError == true);
        Assert.Equal(entries.Count, call.StructuredContent!.Value.GetProperty("total").GetInt32());
        string cursor = call.StructuredContent.Value.GetProperty("nextCursor").GetString()!;
        Assert.False((await PlanTools.GetAsync(_store, _trust, _tasks, _server, record.Definition.PlanId, "detailed", 1, cursor, Token)).IsError == true);
    }

    private static SnapshotPage FarPage(string name, long oid)
    {
        SnapshotPage page = PlanScopeTestData.Baseline([], [name]).Snapshot.Pages[0];
        page.Oid = oid;
        page.Objects[0].Oid = oid;
        return page;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version2_CoverageStorageFails_PreservesSettledExecutionAndMarksUncollected(bool unknown)
    {
        ConfigureLocal();
        await Prepare(local: true);
        if (unknown)
        {
            _bridge.WriteFailure = new()
            {
                Reason = ErrorReason.OutcomeUnknown,
                Message = "Outcome unavailable",
                Execution = ExecutionState.Unknown,
            };
        }
        _coverageFault = checkpoint =>
        {
            if (checkpoint == "evidence_staged")
            {
                string folder = Path.Combine(_locations.State, "plans", _prepared!.Record.Definition.PlanId, "evidence");
                if (Directory.EnumerateFiles(folder, ".partial-*").Any(file => File.ReadAllText(file).Contains("unobservedPages", StringComparison.Ordinal)))
                {
                    throw new IOException("Coverage storage unavailable");
                }
            }
        };
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Single(_bridge.Writes);
        Assert.Equal(unknown ? PlanExecution.Unknown : PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(unknown ? ExecutionState.Unknown : ExecutionState.Committed, result.Steps[0].Execution);
        Assert.Equal(unknown ? ErrorReason.OutcomeUnknown : ErrorReason.Internal, result.Error!.Reason);
        Assert.Equal("skipped", result.Verification);
        Assert.Contains(result.Coverage!.Value.GetProperty("entries").EnumerateArray(), c =>
            c.GetProperty("propertyGaps").EnumerateArray().Any(g => g.GetString() == "evidence.uncollected"));
    }
}
