using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageUpdateFrameTests : IDisposable
{
    private readonly PageFrameStore _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public void Plan_MixedProperties_ListsEachChangedPropertyAndPreservesDescriptions()
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item(description: "Main"), PageFrameStore.Item(PageFrameStore.Second));
        WritePlan plan = Plan(args);

        Assert.Equal([("description (en_US)", "Power", "Main"), ("plot_frame", "Frame_A", "Frame_B")],
            plan.Changes.Select(c => (c.Property, c.OldValue, c.NewValue)));
        Assert.Equal([PageFrameStore.First, PageFrameStore.First], plan.Changes.Select(c => c.Name));
        Assert.Empty(_store.Calls);
        Assert.Empty(_store.FrameWrites);
        Assert.Empty(_store.DescriptionWrites);
    }

    [Fact]
    public void DescriptionOnly_PreservesLegacyFingerprintAndNeverDiscoversFrames()
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item(frame: null, description: "Main"));
        WritePlan plan = Plan(args);
        string expected = Fingerprint.Of([PageFrameStore.First + "\t" + _store.Read(_store.Project.Path, PageFrameStore.First).Raw]);

        Assert.Equal(expected, plan.Fingerprint);
        Assert.Equal(0, _store.DiscoveryCalls);
        PageUpdateData result = Execute(args, plan);
        Assert.Null(Assert.Single(result.Pages).PlotFrame);
        Assert.Empty(_store.FrameWrites);
    }

    [Fact]
    public void InheritedEffectiveFrameAlreadyMatches_DoesNotFixTheSettingOrOpenTransaction()
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item(frame: "Frame_A"));
        WritePlan plan = Plan(args);
        Assert.Empty(plan.Changes);
        PageUpdateData result = Execute(args, plan);

        Assert.Equal(1, result.Unchanged);
        Assert.Empty(result.Pages);
        Assert.Empty(_store.Calls);
        Assert.Empty(_store.FrameWrites);
        Assert.Null(_store.Frames[PageFrameStore.First].Setting);
    }

    [Fact]
    public void UnchangedDescriptionWithChangedFrame_DoesNotRewriteDescription()
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item(description: "Power"));
        WritePlan plan = Plan(args);
        Assert.Equal("plot_frame", Assert.Single(plan.Changes).Property);
        PageUpdateData data = Execute(args, plan);
        Assert.Equal(0, data.Unchanged);
        Assert.Equal("Power", Assert.Single(data.Pages).Description);
        Assert.Empty(_store.DescriptionWrites);
        Assert.Single(_store.FrameWrites);
    }

    [Fact]
    public void MixedExecute_WritesOnlyChangingPropertiesAndCommitsOneUndoStep()
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item(description: "Main"), PageFrameStore.Item(PageFrameStore.Second), PageFrameStore.Item(PageFrameStore.Third));
        PageUpdateData result = Execute(args, Plan(args));

        Assert.Equal(1, result.Unchanged);
        Assert.Equal([PageFrameStore.First, PageFrameStore.Third], result.Pages.Select(p => p.Name));
        Assert.Equal(("Main", "Power", "Frame_B", "Frame_A"),
            (result.Pages[0].Description, result.Pages[0].Previous, result.Pages[0].PlotFrame, result.Pages[0].PreviousPlotFrame));
        Assert.Null(result.Pages[1].Description);
        Assert.Equal([PageFrameStore.First], _store.DescriptionWrites);
        Assert.Equal([PageFrameStore.First, PageFrameStore.Third], _store.FrameWrites);
        Assert.Contains(_store.Descriptions[PageFrameStore.First], e => e.Language == "de_DE" && e.Text == "Netz");
        Assert.Equal(["Begin AI: eplan_page_update", "Commit", "CloseUndo", "Dispose"], _store.Calls);
    }

    [Fact]
    public void UnknownFrame_HasBoundedAvailableNamesAndCompleteCount()
    {
        _store.AvailableOverride = [.. Enumerable.Range(0, 35).Select(i => "Frame_" + i.ToString("D2", CultureInfo.InvariantCulture) + new string('x', 250))];
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(PageFrameStore.Args(PageFrameStore.Item(frame: "missing")))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal("35", error.Metadata!["availableCount"]);
        Assert.Equal(20, error.Metadata["available"].Split('\n').Length);
        Assert.All(error.Metadata["available"].Split('\n'), n => Assert.Equal(200, n.Length));
        Assert.Empty(_store.Calls);
    }

    [Theory]
    [InlineData("setting")]
    [InlineData("effective")]
    [InlineData("identity")]
    [InlineData("available")]
    [InlineData("targetGone")]
    [InlineData("pageGone")]
    [InlineData("description")]
    public void Execute_StaleFrameEvidence_RefusesBeforeTransaction(string change)
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item());
        WritePlan plan = Plan(args);
        switch (change)
        {
            case "setting":
                _store.Frames[PageFrameStore.First] = new("page:1", "Frame_A", "Frame_A");
                break;
            case "effective":
                _store.Frames[PageFrameStore.First] = new("page:1", null, "Frame_B");
                break;
            case "identity":
                _store.Frames[PageFrameStore.First] = new("replacement-page", null, "Frame_A");
                break;
            case "available":
                _store.AvailableOverride = ["Frame_A", "Frame_B", "Frame_C"];
                break;
            case "targetGone":
                _store.Frames.Remove(PageFrameStore.Second);
                break;
            case "pageGone":
                _store.Frames.Remove(PageFrameStore.First);
                break;
            case "description":
                _store.Descriptions[PageFrameStore.First].Add(new("zh_CN", "电源"));
                break;
        }

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args, plan)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_store.Calls);
        Assert.Empty(_store.FrameWrites);
    }

    [Theory]
    [InlineData("setting")]
    [InlineData("effective")]
    [InlineData("identity")]
    [InlineData("description")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public void LaterPageFailureOrCancel_RollsBackAllFramesAndDescriptions(string failure)
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item(description: "Main"), PageFrameStore.Item(PageFrameStore.Third));
        WritePlan plan = Plan(args);
        var context = new OperationContext("test", write: true, progress: null);
        _store.Tamper = state => _store.FrameWrites.Count < 2 ? state : failure switch
        {
            "setting" => new(state.Identifier, null, state.Effective),
            "effective" => new(state.Identifier, state.Setting, "Frame_A"),
            "identity" => new("replacement-page", state.Setting, state.Effective),
            _ => state,
        };
        _store.FailPage = failure == "throw" ? PageFrameStore.Third : null;
        _store.AfterFrameWrite = () =>
        {
            if (_store.FrameWrites.Count == 2 && failure == "cancel")
            {
                context.Cancel();
            }

            if (_store.FrameWrites.Count == 2 && failure == "description")
            {
                _store.Descriptions[PageFrameStore.Third] = [new("en_US", "unexpected")];
            }
        };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(args, plan))).Error;
        Assert.Equal(ExecutionState.RolledBack, error.Execution);
        Assert.Equal("Frame_A", _store.Frames[PageFrameStore.First].Effective);
        Assert.Null(_store.Frames[PageFrameStore.First].Setting);
        Assert.Equal("Frame_A", _store.Frames[PageFrameStore.Third].Effective);
        Assert.Equal("Power", _store.Read(_store.Project.Path, PageFrameStore.First).TextIn("en_US"));
        Assert.Empty(_store.Descriptions[PageFrameStore.Third]);
        Assert.Equal(["Begin AI: eplan_page_update", "CloseUndo", "Rollback", "Dispose"], _store.Calls);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void PageCap_RefusesBeforeFrameDiscovery(int count, bool valid)
    {
        PageUpdateArgs args = PageFrameStore.Args([.. Enumerable.Range(1, count).Select(n =>
        {
            string name = "=P/" + n.ToString(CultureInfo.InvariantCulture);
            _store.Frames[name] = new(name, null, "Frame_A");
            _store.Descriptions[name] = [];
            return PageFrameStore.Item(name);
        })]);
        if (valid)
        {
            Assert.Equal(count, Plan(args).Changes.Count);
        }
        else
        {
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
            Assert.Equal(0, _store.DiscoveryCalls);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ", "Power")]
    public void InvalidItem_RefusesBeforeDiscovery(string? frame, string? description)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(PageFrameStore.Args(PageFrameStore.Item(frame: frame, description: description)))).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("pages[0]", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Equal(0, _store.DiscoveryCalls);
    }

    [Fact]
    public void DuplicatePagesAndFrameCase_RefuseWithoutWriting()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(PageFrameStore.Args(
            PageFrameStore.Item(), PageFrameStore.Item(PageFrameStore.First.ToLowerInvariant())))).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(PageFrameStore.Args(PageFrameStore.Item(frame: "frame_b")))).Error.Reason);
        Assert.Empty(_store.FrameWrites);
    }

    [Fact]
    public void Fingerprint_AvailableEnumerationOrderDoesNotMatterAndRawStateIsNotDelimited()
    {
        PageUpdateArgs args = PageFrameStore.Args(PageFrameStore.Item());
        WritePlan before = Plan(args);
        _store.AvailableOverride = ["Frame_B", "Frame_A", "Frame_A"];
        Assert.Equal(before.Fingerprint, Plan(args).Fingerprint);
        _store.Frames[PageFrameStore.First] = new("page:1", "a\nb\t;@", "Frame_A");
        Assert.NotEqual(before.Fingerprint, Plan(args).Fingerprint);
    }

    [Fact]
    public void Inspect_ReadsRawAndEffectiveValuesAndRejectsUnboundedRequests()
    {
        var operation = new PagePlotFrameInspectOperation(_store, () => _store);
        var args = new ProjectSnapshotArgs { Project = "Line1", Pages = [PageFrameStore.First, PageFrameStore.Second] };
        var states = (Dictionary<string, PagePlotFrameState>)operation.Execute(args)!;
        Assert.Null(states[PageFrameStore.First].Setting);
        Assert.Equal("Frame_A", states[PageFrameStore.First].Effective);
        Assert.Equal("Frame_B", states[PageFrameStore.Second].Setting);
        args.Pages = [];
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error.Reason);
    }

    private WritePlan Plan(PageUpdateArgs args) => (WritePlan)new PageUpdatePlanOperation(_store, _store, _store, () => _store).Execute(args)!;

    private PageUpdateData Execute(PageUpdateArgs args, WritePlan plan)
    {
        args.Fingerprint = plan.Fingerprint;
        return (PageUpdateData)new PageUpdateExecuteOperation(_store, _store, _store, _store, () => _store).Execute(args)!;
    }
}
