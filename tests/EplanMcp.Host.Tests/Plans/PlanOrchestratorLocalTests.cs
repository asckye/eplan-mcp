using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

public sealed partial class PlanOrchestratorTests
{
    private string? _localFault;

    [Fact]
    public async Task Version2_DryRunAndApprovalRechecks_OnlyReadStoredPagesNamesAndClosure()
    {
        await PublishLocal();
        string before = await File.ReadAllTextAsync(RecordFile, Token);
        using (PlanPreparation dry = await Prepare(dryRun: true))
        {
            Assert.Null(dry.Task);
            Assert.Equal(before, await File.ReadAllTextAsync(RecordFile, Token));
            AssertLocalReads(1);
            await AssertUnused();
        }
        _bridge.Calls.Clear();
        _channel = new("fake", ApprovalDecision.Granted) { Wait = () => AssertLocalReads(1) };
        _ready = await Prepare();

        Assert.Equal(PlanStatus.Running, _ready.Record.State.Status);
        Assert.Single(_channel.Questions);
        AssertLocalReads(2);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.Empty(_bridge.Writes);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("missingPage")]
    [InlineData("names")]
    [InlineData("closure")]
    [InlineData("replace")]
    [InlineData("main")]
    [InlineData("pageLessAdded")]
    [InlineData("project")]
    public async Task Version2_DryRunChangedEvidence_IsStaleAndConsumesNothing(string fault)
    {
        await PublishLocal();
        _localFault = fault;
        string before = await File.ReadAllTextAsync(RecordFile, Token);
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Prepare(dryRun: true));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Equal(before, await File.ReadAllTextAsync(RecordFile, Token));
        Assert.Equal(["plan_compiled"], Records().Select(Kind));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.DoesNotContain(_bridge.Calls, c => c.Args is ProjectSnapshotArgs { Pages: null });
        await AssertUnused();
    }

    [Theory]
    [InlineData("page", false)]
    [InlineData("names", false)]
    [InlineData("closure", false)]
    [InlineData("page", true)]
    [InlineData("names", true)]
    [InlineData("closure", true)]
    public async Task Version2_ChangedBeforeOrDuringApproval_ConsumesPlanWithoutBackup(string fault, bool duringApproval)
    {
        await PublishLocal();
        if (duringApproval)
        {
            _channel = new("fake", ApprovalDecision.Granted) { Wait = () => _localFault = fault };
        }
        else
        {
            _localFault = fault;
        }
        using PlanPreparation failed = await Prepare();

        await AssertFailure(failed, ErrorReason.PlanStale);
        Assert.Equal(duringApproval ? 1 : 0, _channel.Questions.Count);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.DoesNotContain(_bridge.Calls, c => c.Args is ProjectSnapshotArgs { Pages: null });
        Assert.Equal(ErrorReason.InvalidArgument, (await Assert.ThrowsAsync<BridgeCallException>(() => Prepare())).Error.Reason);
    }

    [Fact]
    public async Task Version2_PageCreate_EmptyStoredPages_NeverSendsSnapshot()
    {
        await PublishLocal(pageCreate: true);
        using PlanPreparation dry = await Prepare(dryRun: true);

        Assert.DoesNotContain(_bridge.Calls, c => c.Operation is OperationNames.ProjectSnapshot or OperationNames.PlanFunctions);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.PlanNames);
        await AssertUnused();
    }

    [Fact]
    public async Task Version1_DryRun_StillUsesWholeProjectDoubleReadAndDeviceList()
    {
        await Publish();
        using PlanPreparation dry = await Prepare(dryRun: true);

        Assert.Equal(2, _bridge.Calls.Count(c => c.Args is ProjectSnapshotArgs { Pages: null }));
        Assert.Equal(2, _bridge.Calls.Count(c => c.Operation == OperationNames.DeviceList));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.PlanNames);
        await AssertUnused();
    }

    [Fact]
    public async Task Version2_StepOnly_SkipsBaselineReads()
    {
        await PublishLocal();
        PlanRecord record = (await _store.ReadAsync(_definition.PlanId, Token))!;
        var packs = new Host.Packs.PackLibrary(_locations, _config, _audit, _clock);
        _ = await new PlanApplyChecks(_bridge, packs, _store, _trust).CheckAsync(record, true, Token);

        Assert.Empty(_bridge.Calls);
    }

    [Theory]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public async Task Version2_Renumber_ProjectGrowsPastFiftyPages_RefusesBeforeContentRead(string op)
    {
        await PublishLocal(renumber: op);
        _localFault = "renumberPages";
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Prepare(dryRun: true));

        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Equal("plan.renumber:whole_project_guard", error.Error.Metadata!["missing"]);
        Assert.Equal("51", error.Error.Metadata["pages"]);
        Assert.Equal(OperationNames.PlanNames, Assert.Single(_bridge.Calls).Operation);
        await AssertUnused();
    }

    private void AssertLocalReads(int count)
    {
        Assert.Equal(count, _bridge.Calls.Count(c => c.Operation == OperationNames.PlanNames));
        Assert.Equal(count, _bridge.Calls.Count(c => c.Operation == OperationNames.PlanFunctions));
        Assert.Equal(count, _bridge.Calls.Count(c => c.Operation == OperationNames.ProjectSnapshot));
        Assert.All(_bridge.Calls.Where(c => c.Operation == OperationNames.ProjectSnapshot), c =>
            Assert.Equal(["+A1/1", "+A1/2"], Assert.IsType<ProjectSnapshotArgs>(c.Args).Pages));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.DeviceList);
    }

    private async Task PublishLocal(bool pageCreate = false, string? renumber = null)
    {
        PlanBaseline source = PlanScopeTestData.Baseline(project: _definition.Project.Path);
        source = source with { Snapshot = source.Snapshot with { Project = _definition.Project } };
        _bridge.Projects.Add(new() { Name = source.Snapshot.Project.Name, Path = source.Snapshot.Project.Path });
        _bridge.Status.Handshake!.Operations = [.. _bridge.Status.Handshake.Operations!, OperationNames.PlanNames, OperationNames.PlanFunctions,
            OperationNames.DeviceUpdatePlan, OperationNames.DeviceUpdateExecute];
        _bridge.Handlers[OperationNames.PlanNames] = _ =>
        {
            PlanOccupiedName[] names = [.. source.Names, .. _localFault == "names" ? new[] { new PlanOccupiedName("function", "+REMOTE-NEW") } : []];
            if (_localFault == "renumberPages")
            {
                names = [.. names, .. Enumerable.Range(1, 48).Select(i => new PlanOccupiedName("page", "+REMOTE/" + i))];
            }
            return new PlanNamesData
            {
                Project = _localFault == "project" ? "Other" : source.Snapshot.Project.Name,
                ProjectPath = source.Snapshot.Project.Path,
                ProjectGuid = source.Snapshot.Project.Guid,
                Complete = true,
                Names = [.. names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })],
                NamesHash = PlanSnapshotHashes.Names(source.Snapshot.Project.Path, names),
            };
        };
        _bridge.Handlers[OperationNames.PlanFunctions] = args =>
        {
            PlanFunctionsArgs request = Assert.IsType<PlanFunctionsArgs>(args);
            Assert.Null(request.Pages);
            var data = new PlanFunctionsData
            {
                Project = source.Snapshot.Project.Name,
                ProjectPath = source.Snapshot.Project.Path,
                Complete = true,
                Limit = 10_000,
                Functions = [.. PlanScopeTestData.Functions(source).Where(f => request.Names!.Contains(f.Name, StringComparer.Ordinal))],
            };
            if (_localFault is "closure" or "pageLessAdded")
            {
                data.Functions.Add(new() { Type = 17, Oid = 999, Name = "+A1-K1", Page = _localFault == "closure" ? "+A10/1" : null });
            }
            if (_localFault == "replace")
            {
                data.Functions[0].Oid = 999;
            }
            if (_localFault == "main")
            {
                data.Functions[0].IsMain = false;
            }
            return data;
        };
        _bridge.Handlers[OperationNames.ProjectSnapshot] = args =>
        {
            ProjectSnapshotArgs request = Assert.IsType<ProjectSnapshotArgs>(args);
            Assert.NotNull(request.Pages);
            List<SnapshotPage> pages = JsonSerializer.Deserialize<List<SnapshotPage>>(JsonSerializer.Serialize(source.Snapshot.Pages
                .Where(p => request.Pages.Contains(p.Name, StringComparer.Ordinal))))!;
            if (_localFault == "page")
            {
                pages[0].Objects[0].Hash = "changed-description";
                pages[0].Hash = PlanSnapshotHashes.Page(pages[0].Objects);
            }
            if (_localFault == "missingPage")
            {
                pages.RemoveAt(0);
            }
            return new ProjectSnapshotData
            {
                Project = source.Snapshot.Project.Name,
                ProjectPath = source.Snapshot.Project.Path,
                ProjectGuid = source.Snapshot.Project.Guid,
                Profile = SnapshotProfiles.StandardName,
                Format = SnapshotProfiles.Format,
                Complete = true,
                PageCount = pages.Count,
                Pages = pages,
                MissingPages = [.. request.Pages.Except(pages.Select(p => p.Name), StringComparer.Ordinal)],
            };
        };
        var plan = new WritePlan
        {
            Project = source.Snapshot.Project.Name,
            ProjectPath = source.Snapshot.Project.Path,
            Fingerprint = "local-session-fingerprint",
            Changes = [new() { Action = "update", Kind = "device", Name = "+A1-K1", Property = "tag", OldValue = "+A1-K1", NewValue = "+A1-K9" }],
        };
        _bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ => plan;
        PlanStep? compiled = null;
        PlanBaselineRead read = await new PlanLocalBaselineReader(_bridge).ReadAsync(source.Snapshot.Project.Path,
            [new(renumber ?? (pageCreate ? "page.create" : "device.update"), PlanScopeRules.Pages(renumber is not null ? ["+A1/1"] : pageCreate ? ["+A/1"] : []), "+A1-K1")], (baseline, impacts) =>
            {
                if (pageCreate)
                {
                    compiled = _definition.Steps[0];
                }
                else
                {
                    PlanImpact impact = PlanLocalScopeRules.Build(baseline, Assert.Single(impacts), plan);
                    compiled = new(0, renumber ?? "device.update", renumber is not null ? PlanTestData.Json("""{"pages":["+A1/1"]}""")
                        : PlanTestData.Json("""{"tag":"+A1-K1","new_tag":"-K9"}"""), ToolTier.Destructive,
                        impact.ReadScope, impact.ModifyScope, impact.Preconditions, [],
                        [.. plan.Changes.Select(c => new PlanChange(c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue))], [], null, true);
                }
                return Task.CompletedTask;
            }, Token);
        PlanBaseline stable = Assert.IsType<PlanBaseline>(read.Baseline);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(stable, PlanJson.Options);
        string hash = PlanStore.Hash(bytes);
        await Publish(modify: d => d with
        {
            CompilerVersion = "2",
            ContextRef = "sha256:" + hash,
            Steps = [compiled!],
            Inputs = new([new(compiled!.Op, compiled.Args)]),
            Digests = new(stable.Snapshot.ComposedHash, stable.Snapshot.MembersHash, stable.NamesHash, null),
            RequiredOperations = [.. _bridge.Status.Handshake.Operations!],
        }, evidence: new() { [hash] = bytes });
        _bridge.Calls.Clear();
    }
}
