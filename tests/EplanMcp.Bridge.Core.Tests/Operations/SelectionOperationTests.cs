using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class SelectionOperationTests
{
    internal static DrawingLocator Locator(long oid = 1, string project = @"D:\scratch\copy", string page = "=A/1") => new()
    { Project = project, Page = page, Type = 17, Oid = oid, Digest = new string('a', 64) };
    private readonly OpenProject _project = new() { Path = @"D:\scratch\copy", Name = "Scratch", Current = true };
    private readonly Selection _selection = new();
    private SelectionOperation Operation(bool set = true) => new(new FakeCatalog(_project), _selection, set ? SelectionKind.Select : SelectionKind.Read);
    private SelectionData Execute(SelectionArgs args) => (SelectionData)Operation().Execute(args)!;

    [Fact]
    public void Replace_Add_Clear_AreIdempotentAndDoNotUseWriteTransactions()
    {
        var first = Locator();
        var second = Locator(2);
        Assert.True(Execute(new() { Mode = "replace", Locators = [first] }).Exact);
        Assert.True(Execute(new() { Mode = "add", Locators = [second] }).Exact);
        Assert.True(Execute(new() { Mode = "add", Locators = [second] }).Exact);
        Assert.Equal(2, _selection.Current.Objects.Count);
        Assert.True(Execute(new() { Mode = "clear" }).Exact);
        Assert.Empty(_selection.Current.Objects);
        Assert.False((IOperation)Operation() is IWriteOperation);
    }

    [Fact]
    public void InvalidBatch_IsRejectedBeforeAnyUiMutation()
    {
        List<SelectionArgs> cases = [new() { Mode = "replace" }, new() { Mode = "wrong" }, new() { Mode = "clear", ShowPage = true },
            new() { Mode = "clear", Locators = [Locator()] }, new() { Mode = "replace", Locators = [Locator(), Locator()] },
            new() { Mode = "replace", Locators = [Locator(), Locator(2, page: "=A/2")] },
            new() { Mode = "replace", Locators = [Locator(), Locator(2, project: @"D:\real")] },
            new() { Mode = "replace", Locators = [new() { Digest = "invalid" }] }];
        foreach (SelectionArgs args in cases)
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason); }
        Assert.Equal(0, _selection.Mutations);
    }

    [Fact]
    public void NonCurrentProject_RequiresShowPage_AndPreflightFailureNeverOpens()
    {
        _project.Current = false;
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Execute(new() { Mode = "replace", Locators = [Locator()] })).Error.Reason);
        Assert.Equal(0, _selection.Mutations);
        Assert.True(Execute(new() { Mode = "replace", Locators = [Locator()], ShowPage = true }).Exact);
        _selection.Stale = true;
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(new() { Mode = "replace", Locators = [Locator()], ShowPage = true })).Error.Reason);
        Assert.Equal(1, _selection.Mutations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncompatibleExistingSelection_BlocksAdd(bool unsupported)
    {
        _selection.Current.Unsupported = unsupported ? 1 : 0;
        if (!unsupported)
        { _selection.Current.Objects.Add(new() { Locator = Locator(2, page: "=A/2") }); }
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Execute(new() { Mode = "add", Locators = [Locator()] })).Error.Reason);
        Assert.Equal(0, _selection.Mutations);
    }

    [Fact]
    public void PartialReadback_ReportsEveryMissingLocator_AndExtrasPreventExact()
    {
        _selection.Drop = true;
        var result = Execute(new() { Mode = "replace", Locators = [Locator(), Locator(2)] });
        Assert.False(result.Exact);
        Assert.Equal(2, Assert.Single(result.Unselected).Oid);
        _selection.Drop = false;
        _selection.Extra = true;
        Assert.False(Execute(new() { Mode = "replace", Locators = [Locator()] }).Exact);
    }

    [Fact]
    public void ReplaceRectangle_ShowPageWithNavigatorActive_FocusesGedBeforeExactReadback()
    {
        // Large ProductSelection: two rectangles at [40,40]..[80,80] and [100,40]..[140,80].
        // SelectObjects changes GED selection, while SelectionSet still reports the active navigator's Project.
        double[][][] rectangles = [[[40, 40], [80, 80]], [[100, 40], [140, 80]]];
        DrawingLocator[] locators = [.. rectangles.Select((corners, i) => new DrawingLocator
        {
            Project = _project.Path, Page = "=A/1", Type = 31, Oid = i + 1,
            Digest = SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(corners)),
        })];
        DrawingLocator first = locators[0], second = locators[1];
        _selection.NavigatorActive = true;
        SelectionData result = Execute(new() { Mode = "replace", Locators = [first], ShowPage = true });
        Assert.True(result.Exact);
        Assert.Empty(result.Unselected);
        Assert.Same(first, Assert.Single(result.Objects).Locator);
        Assert.True(Execute(new() { Mode = "add", Locators = [second] }).Exact);
        Assert.Equal(2, _selection.Current.Objects.Count);
        Assert.True(Execute(new() { Mode = "clear" }).Exact);
        Assert.Equal(3, _selection.Focuses);
    }

    [Fact]
    public void FocusFailure_ReportsUiErrorWithoutAssumingTheRequestedSelection()
    {
        _selection.FocusSucceeds = false;
        var error = Assert.Throws<BridgeErrorException>(() => Execute(new() { Mode = "replace", Locators = [Locator()], ShowPage = true })).Error;
        Assert.Equal(ErrorReason.EplanOperationFailed, error.Reason);
        Assert.Null(error.Execution);
        Assert.Equal(1, _selection.Mutations);
        // Clear with no editor still independently reads the navigator, so it cannot claim an exact empty selection.
        _selection.NavigatorActive = true;
        Assert.False(Execute(new() { Mode = "clear" }).Exact);
    }

    [Fact]
    public void Read_IsBoundedAndFingerprintCoversTheCompleteSelection()
    {
        _selection.Current.Objects = Enumerable.Range(1, 250).Select(i => new ObjectSummary { Locator = Locator(i) }).ToList();
        SelectionData first = (SelectionData)Operation(false).Execute(new SelectionArgs())!;
        SelectionData last = (SelectionData)Operation(false).Execute(new SelectionArgs { Offset = 200 })!;
        Assert.Equal((250, 200, 50), (first.Total, first.Objects.Count, last.Objects.Count));
        Assert.Equal(first.Fingerprint, last.Fingerprint);
        _selection.Current.Objects[240].Locator.Digest = new string('b', 64);
        Assert.NotEqual(first.Fingerprint, ((SelectionData)Operation(false).Execute(new SelectionArgs())!).Fingerprint);
    }

    [Fact]
    public void Offline_RejectsWithoutProjectAccess()
    {
        var error = Assert.Throws<BridgeErrorException>(() => new SelectionOperation(new FakeCatalog(), null, SelectionKind.Read).Execute(new SelectionArgs())).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
        Assert.Null(error.Execution);
    }

    [Fact]
    public void NullReadLocators_ReturnInvalidArgument()
    {
        var error = Assert.Throws<BridgeErrorException>(() => Operation(false).Execute(new SelectionArgs { Locators = null! })).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
    }

    internal sealed class Selection : ISelectionAdapter
    {
        public SelectionData Current { get; set; } = new() { Project = @"D:\scratch\copy", Page = "=A/1" };
        public bool Stale { get; set; }
        public bool Drop { get; set; }
        public bool Extra { get; set; }
        public bool NavigatorActive { get; set; }
        public bool FocusSucceeds { get; set; } = true;
        public int Focuses { get; private set; }
        public int Mutations { get; private set; }
        public SelectionData Read() => new()
        {
            Project = Current.Project,
            Page = Current.Page,
            Unsupported = Current.Unsupported,
            Objects = NavigatorActive ? [new() { Locator = new() { Project = Current.Project!, Type = 14, Oid = 1, Digest = new string('b', 64) } }] : Current.Objects.ToList(),
        };
        public bool FocusEditor()
        {
            Focuses++;
            if (FocusSucceeds)
            { NavigatorActive = false; }
            return FocusSucceeds;
        }
        public void Validate(string project, IReadOnlyList<DrawingLocator> locators)
        { if (Stale) { throw new BridgeErrorException(ErrorReason.PlanStale, "Object changed."); } }
        public void Apply(string? project, IReadOnlyList<DrawingLocator> locators, bool showPage)
        {
            Mutations++;
            Current.Objects = locators.Take(Drop ? 1 : locators.Count).Select(l => new ObjectSummary { Locator = l }).ToList();
            if (Extra)
            { Current.Objects.Add(new() { Locator = Locator(999) }); }
        }
    }
}
