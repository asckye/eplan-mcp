using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

public sealed partial class PlanExecutionTests
{
    [Fact]
    public async Task Version2_Execution_OnlyReadsEachFixedE_StoresBoundedDigestsAndReusesContents()
    {
        ConfigureLocal();
        await Prepare(local: true);
        _bridge.Calls.Clear();
        PlanRecord record = await _service.ExecuteAsync(_server, _prepared!, null, Token);
        PlanResult result = record.State.Result!;

        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal("passed", result.Verification);
        Assert.Equal(3, _bridge.Writes.Count);
        Assert.Equal(["+A1/3", "+A1/3", "+A1/3", "+A1/1", "+A1/1"],
            _bridge.Calls.Where(c => c.Args is ProjectSnapshotArgs).Select(c => Assert.Single(Assert.IsType<ProjectSnapshotArgs>(c.Args).Pages!)));
        Assert.Equal(6, _bridge.Calls.Count(c => c.Operation == OperationNames.PlanNames));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.DeviceList);
        JsonElement references = result.Coverage!.Value.GetProperty("evidence");
        Assert.Equal(27, references.EnumerateObject().Count());
        Assert.Equal(references.GetProperty("1:after").GetString(), references.GetProperty("2:before").GetString());
        Assert.Equal(references.GetProperty("1:after:names").GetString(), references.GetProperty("2:after:names").GetString());
        Assert.Equal(references.GetProperty("1:before:expectedNames").GetString(), references.GetProperty("1:after:names").GetString());
        foreach (JsonProperty reference in references.EnumerateObject())
        {
            byte[] bytes = await _store.ReadEvidenceAsync(record.Definition.PlanId, reference.Value.GetString()!, Token);
            using JsonDocument parsed = JsonDocument.Parse(bytes);
            if (reference.Name.EndsWith(":before", StringComparison.Ordinal) || reference.Name.EndsWith(":after", StringComparison.Ordinal))
            {
                Assert.False(parsed.RootElement.GetProperty("wholeProject").GetBoolean());
                Assert.Equal(JsonValueKind.Null, parsed.RootElement.GetProperty("snapshot").GetProperty("snapshotHash").ValueKind);
            }
            else if (!reference.Name.EndsWith(":coverage", StringComparison.Ordinal))
            {
                Assert.Equal(4, parsed.RootElement.EnumerateObject().Count());
                Assert.Equal(64, parsed.RootElement.GetProperty("names").GetString()!.Length);
                Assert.True(bytes.Length < 256);
            }
        }
        Assert.DoesNotContain(_bridge.Calls, c => c.Args is ProjectSnapshotArgs { Pages: null });
    }

    [Fact]
    public async Task Version1_Execution_StillReadsWholeProjectBeforeAndAfterEveryStep()
    {
        await Prepare();
        _bridge.Calls.Clear();
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal(6, _bridge.Calls.Count(c => c.Args is ProjectSnapshotArgs { Pages: null }));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.PlanNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version2_EvidenceBudgetExhausted_StopsAndPersistsKnownFactsAndUncollectedCoverage(bool afterCommit)
    {
        ConfigureLocal();
        await Prepare(local: true);
        if (afterCommit)
        {
            _after = _ => FillEvidenceBudget();
        }
        else
        {
            FillEvidenceBudget();
        }
        PlanRecord record = await _service.ExecuteAsync(_server, _prepared!, null, Token);
        PlanResult result = record.State.Result!;

        Assert.Equal(afterCommit ? PlanExecution.PartiallyCommitted : PlanExecution.NotStarted, result.Execution);
        Assert.Equal(ErrorReason.ResourceExhausted, result.Error!.Reason);
        Assert.Equal("skipped", result.Verification);
        Assert.Equal(afterCommit ? 1 : 0, _bridge.Writes.Count);
        Assert.Equal(afterCommit ? ExecutionState.Committed : ExecutionState.NotStarted, result.Steps[0].Execution);
        Assert.All(result.Steps.Skip(1), s => Assert.Equal(ExecutionState.NotStarted, s.Execution));
        Assert.Contains(result.Coverage!.Value.GetProperty("entries").EnumerateArray(), c =>
            c.GetProperty("window").GetString() == (afterCommit ? "after:uncollected" : "before:uncollected")
            && c.GetProperty("propertyGaps").EnumerateArray().Any(g => g.GetString() == "evidence.uncollected"));
        Assert.Equal(result.Execution, (await _store.ReadAsync(record.Definition.PlanId, Token))!.State.Result!.Execution);
        Assert.Empty(_suspensions.All());
    }

    [Fact]
    public async Task Version2_EvidenceBudgetExhaustedAfterLastCommit_KeepsFullyCommittedExecution()
    {
        ConfigureLocal();
        await Prepare(local: true);
        _after = count =>
        {
            if (count == 3)
            {
                FillEvidenceBudget();
            }
        };
        PlanRecord record = await _service.ExecuteAsync(_server, _prepared!, null, Token);

        Assert.Equal(PlanExecution.Committed, record.State.Result!.Execution);
        Assert.Equal(ErrorReason.ResourceExhausted, record.State.Result.Error!.Reason);
        Assert.Equal("skipped", record.State.Result.Verification);
        Assert.Equal(3, _bridge.Writes.Count);
        Assert.All(record.State.Result.Steps, s => Assert.Equal(ExecutionState.Committed, s.Execution));
        Assert.Equal(PlanExecution.Committed, (await _store.ReadAsync(record.Definition.PlanId, Token))!.State.Result!.Execution);
    }

    [Fact]
    public async Task Version2_DeletedPage_AfterAbsenceIsApprovedEvidence()
    {
        ConfigureLocal();
        await Prepare(destructive: true, local: true);
        _bridge.Handlers[OperationNames.PlanStepExecute] = _ =>
        {
            _current = PlanVerificationTestData.Copy(_current, pages => pages.Clear());
            return JsonSerializer.SerializeToElement(new { });
        };
        _bridge.Calls.Clear();
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal("passed", result.Verification);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectSnapshot);
        Assert.DoesNotContain(_bridge.Calls, c => c.Args is ProjectSnapshotArgs { Pages: null });
    }

    [Fact]
    public async Task Version2_CreatedPageMissingAfterCommit_KeepsCommitAndStops()
    {
        ConfigureLocal();
        _after = _ => _current = PlanVerificationTestData.Copy(_current, pages => pages.RemoveAll(p => p.Name == "+A1/3"));
        await Prepare(local: true);
        PlanResult result = (await _service.ExecuteAsync(_server, _prepared!, null, Token)).State.Result!;

        Assert.Equal(PlanExecution.PartiallyCommitted, result.Execution);
        Assert.Equal(ErrorReason.PlanStale, result.Error!.Reason);
        Assert.Single(_bridge.Writes);
        Assert.Equal(ExecutionState.Committed, result.Steps[0].Execution);
    }

    private void FillEvidenceBudget()
    {
        string assets = Path.Combine(_locations.State, "plans", _prepared!.Record.Definition.PlanId);
        long used = Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        using var stream = new FileStream(Path.Combine(assets, "full.bin"), FileMode.CreateNew);
        stream.SetLength(13 * PlanCapacity.MiB - used - 1);
    }

    private void ConfigureLocal()
    {
        _bridge.Status.Handshake!.Operations!.Add(OperationNames.PlanNames);
        _bridge.Handlers[OperationNames.PlanNames] = _ =>
        {
            PlanOccupiedName[] names = [.. _current.Pages.SelectMany(p => p.Objects).Where(o => o.Kind is "page" or "function")
                .Select(o => new PlanOccupiedName(o.Kind, o.Name!)).Distinct()];
            return new PlanNamesData
            {
                Project = _current.Project.Name,
                ProjectPath = _current.Project.Path,
                ProjectGuid = _current.Project.Guid,
                Complete = true,
                NamesHash = PlanSnapshotHashes.Names(_current.Project.Path, names),
                Names = [.. names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })],
            };
        };
        _bridge.Handlers[OperationNames.ProjectSnapshot] = args =>
        {
            ProjectSnapshotArgs request = Assert.IsType<ProjectSnapshotArgs>(args);
            Assert.NotNull(request.Pages);
            Assert.NotEmpty(request.Pages);
            List<SnapshotPage> pages = [.. _current.Pages.Where(p => request.Pages.Contains(p.Name, StringComparer.Ordinal))];
            return new ProjectSnapshotData
            {
                Project = _current.Project.Name,
                ProjectPath = _current.Project.Path,
                ProjectGuid = _current.Project.Guid,
                Profile = _current.Profile,
                Format = _current.Format,
                Pages = pages,
                PageCount = pages.Count,
                Complete = true,
                MissingPages = [.. request.Pages.Except(pages.Select(p => p.Name), StringComparer.Ordinal)],
            };
        };
    }
}
