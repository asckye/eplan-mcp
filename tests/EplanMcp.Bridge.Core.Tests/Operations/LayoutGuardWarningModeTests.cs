using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutGuardWarningModeTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("refuse", false)]
    [InlineData("warn", true)]
    public void LiveSequence337_LineInsideOverviewFrame_OnlyWarnModeAccepts(string? mode, bool accepted)
    {
        // Exact failing geometry from sequence 337; project identity is a test fixture.
        var model = new LayoutGuard.Model
        {
            Page = new()
            {
                Page = "=K1+A2&EFA1/1",
                PageSize = [420, 297],
                GridSize = 4,
                Placements = [new() { Kind = "Function", Bounds = [0, 16, 420, 292], Location = [0, 16], Locator = new() { Type = 37, Oid = 1 } }]
            },
            Lines = [new() { Points = [[264, 226], [264, 178]], Changed = true }]
        };
        if (accepted)
        { Assert.Contains(LayoutGuard.Enforce(model, [], mode: mode!), s => s.StartsWith("R4", StringComparison.Ordinal)); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => LayoutGuard.Enforce(model, [], mode: mode ?? "refuse")).Error.Reason); }
    }

    [Fact]
    public void WarnMode_ReportsPageMarginButStillRejectsOffPage()
    {
        LayoutGuard.Model model = LayoutGuardTests.Model();
        DrawingPlacement item = LayoutGuard.Proposed("Rectangle", [0, 0], [0, 0, 20, 20]);
        Assert.Contains(LayoutGuard.Enforce(model, [item], mode: "warn"), s => s.StartsWith("R1", StringComparison.Ordinal));
        item.Bounds = [-1, 0, 20, 20];
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => LayoutGuard.Enforce(model, [item], mode: "warn")).Error.Reason);
    }

    [Theory]
    [InlineData("permit")]
    [InlineData(1)]
    [InlineData(true)]
    public void UnknownGuardMode_IsInvalid(object mode)
    { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => LayoutGuard.Mode(JToken.FromObject(mode))).Error.Reason); }

    [Fact]
    public void WarnMode_LeavesPostWriteOffPageRollbackUnchanged()
    {
        LayoutGuard.Model before = LayoutGuardTests.Model();
        var actual = new LayoutGuard.Model
        {
            Page = new()
            {
                PageSize = [100, 100],
                GridSize = 4,
                Placements = [LayoutGuard.Proposed("Rectangle", [100, 40], [100, 40, 104, 44])]
            }
        };
        Assert.Throws<BridgeErrorException>(() => LayoutGuard.AfterWriting(before, actual));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("refuse", false)]
    [InlineData("warn", true)]
    public void MoveIntoExistingOutline_UsesRequestedGuard(string? guard, bool accepted)
    {
        var fixture = new MoveFixture();
        fixture.Root.Kind = "Text";
        fixture.Scene.Page.Placements.Add(MoveFixture.Placement(2, 'b', [40, 40]));
        fixture.Request["moves"]![0]!["delta_mm"] = new JArray(20, 20);
        if (guard is not null)
        { fixture.Request["layout_guard"] = guard; }
        if (accepted)
        { Assert.Contains("R3", (string?)fixture.Plan()["warning"], StringComparison.Ordinal); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error.Reason); }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("refuse", false)]
    [InlineData("warn", true)]
    public void WireCrossingAnOutline_UsesRequestedGuard(string? guard, bool accepted)
    {
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.Placements.Add(new() { Kind = "Function", Location = [16, 8], Bounds = [14, 6, 18, 10], Locator = new() { Type = 17, Oid = 3 } });
        if (guard is not null)
        { fixture.Request["layout_guard"] = guard; }
        if (accepted)
        { Assert.Contains("R4", (string?)fixture.Plan()["warning"], StringComparison.Ordinal); }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => fixture.Plan()).Error.Reason); }
    }

    [Fact]
    public void WarningSummary_PreservesItsExistingLimit()
    {
        string summary = LayoutGuard.Summary(Enumerable.Range(0, 50).Select(i => $"R4: conflict {i}"));
        Assert.Equal(LayoutGuard.SummaryLines + 1, summary.Split('\n').Length);
        Assert.Contains("30 more warnings", summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlacementBatch_WarnModeSurvivesParsingPreflightAndCommit(bool planning)
    {
        var store = new PlacementTextStore();
        var model = new LayoutGuard.Model
        {
            Page = new()
            {
                PageSize = [1000, 1000],
                GridSize = 0.5,
                Placements =
                [new() { Kind = "Function", Location = [20, 20], Bounds = [18, 18, 22, 22] }]
            },
            Config = new() { MarginMm = 0 },
            ReadActual = () => new() { Page = store.Read("project", "P"), Config = new() { MarginMm = 0 } }
        };
        var operation = new PlacementTextCreateExecuteOperation(new FakeCatalog(store.Project), store, store.Transactions, store,
            (_, _) => model, planning: planning);
        var args = new PlacementTextCreateArgs
        {
            Project = "Line1",
            Page = "P",
            LayoutGuard = "warn",
            Items = [new() { Kind = "line", Position = [10, 20], EndPosition = [30, 20] }]
        };
        object parsed = operation.ParseArgs(EplanMcp.Bridge.Core.Protocol.BridgeJson.FromObject(args));
        var result = Assert.IsType<PlacementTextCreateData>(operation.Execute(parsed));
        Assert.Contains("R4", result.Warning, StringComparison.Ordinal);
        Assert.Equal(planning ? 0 : 1, store.Writes);
    }
}
