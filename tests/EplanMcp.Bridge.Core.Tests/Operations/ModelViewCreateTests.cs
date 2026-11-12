using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ModelViewCreateTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private readonly FakeLayoutReader _layout = new();
    private readonly FakePageReader _pages = new();
    private readonly FakeWriter _writer = new();
    private readonly FakeTransactions _transactions = new();

    public ModelViewCreateTests()
    {
        _layout.Record.Spaces.Add(new LayoutSpace
        {
            Designation = "UC1",
            Enclosures = [new LayoutEnclosure { Id = 1, Panel = new LayoutSurface { Id = 11, Width = 749, Height = 570 }, Door = new LayoutSurface { Id = 12, Width = 795, Height = 595 } }],
        });
        _pages.Pages.AddRange([new PageSummary { Name = "=+UC1&EAA/1", Type = "ModelView" }, new PageSummary { Name = "=+UC1&EFS/1", Type = "Circuit" }]);
    }

    [Theory]
    [InlineData("panel", 11L)]
    [InlineData("door", 12L)]
    [InlineData("cabinet", null)]
    public void View_PicksTheRootSurface_AndExecutesInOneTransaction(string view, long? root)
    {
        ModelViewCreateArgs args = Args(view);
        WritePlan plan = Plan(args);
        Assert.Equal("UC1 " + view + ": " + view + " of UC1 at (40, 40), 300 x 230 mm", plan.Changes.Single().NewValue);

        args.Fingerprint = plan.Fingerprint;
        ModelViewCreateData data = Execute(args);

        ModelViewStep step = Assert.Single(_writer.Steps);
        Assert.Equal((root, 5, 1, 0, "UC1", "=+UC1&EAA/1"), (step.RootId, step.Viewpoint, step.Style, step.ScaleSetting, step.Space, step.Page));
        Assert.Equal(("42", 7), (data.ObjectId, data.Added));
        Assert.Equal(["Begin AI: eplan_modelview_create", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Theory]
    [InlineData("1:5", 2, "1:5")]
    [InlineData("1:250", 2, "1:250")]
    [InlineData("fit", 1, "")]
    [InlineData(null, 0, "")]
    public void Scale_IsAutoFitOrManual(string? scale, int setting, string manual)
    {
        ModelViewCreateArgs args = Args("panel");
        args.Scale = scale;
        args.Fingerprint = Plan(args).Fingerprint;
        Execute(args);
        Assert.Equal((setting, manual), (_writer.Steps.Single().ScaleSetting, _writer.Steps.Single().Scale));
    }

    [Theory]
    [InlineData("1:0")]
    [InlineData("1:05")]
    [InlineData("2:1")]
    [InlineData("1:1000")]
    public void BadScale_IsRefused(string scale)
    {
        ModelViewCreateArgs args = Args("panel");
        args.Scale = scale;
        Assert.Contains("'scale' must be one of auto, fit or 1:N", Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void BadValues_AreAllRefusedTogether()
    {
        ModelViewCreateArgs args = Args("side");
        args.Viewpoint = "under";
        args.Style = "sketch";
        args.Width = 5;
        args.X = null;
        string violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"];
        foreach (string expected in new[] { "'view' must be", "'viewpoint' must be", "'style' must be", "'width' is required", "'x' is required" })
        { Assert.Contains(expected, violations, StringComparison.Ordinal); }
    }

    [Theory]
    [InlineData("=+UC1&EFS/1", ErrorReason.InvalidArgument)]
    [InlineData("=+UC1&EAA/9", ErrorReason.PageNotFound)]
    public void PageMustBeAnExistingModelViewPage(string page, string reason)
    {
        ModelViewCreateArgs args = Args("panel");
        args.Page = page;
        Assert.Equal(reason, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void NothingAdded_RollsBack()
    {
        _writer.Added = 0;
        ModelViewCreateArgs args = Args("panel");
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void LayoutChangedAfterThePlan_IsStale()
    {
        ModelViewCreateArgs args = Args("panel");
        args.Fingerprint = Plan(args).Fingerprint;
        _layout.Record.Objects.Add(new LayoutObject { Space = "UC1", Id = 99, Box = [1, 2, 3, 4, 5, 6] });
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_writer.Steps);
    }

    private static ModelViewCreateArgs Args(string view) =>
        new() { Project = "Line1", Space = "UC1", Page = "=+UC1&EAA/1", View = view, X = 40, Y = 40, Width = 300, Height = 230 };

    private WritePlan Plan(ModelViewCreateArgs args) => (WritePlan)new ModelViewCreatePlanOperation(new FakeCatalog(_line1), _layout, _pages).Execute(args)!;

    private ModelViewCreateData Execute(ModelViewCreateArgs args) =>
        (ModelViewCreateData)new ModelViewCreateExecuteOperation(new FakeCatalog(_line1), _layout, _pages, _writer, _transactions).Execute(args)!;

    private sealed class FakeLayoutReader : ILayoutReader
    {
        public LayoutRecord Record { get; } = new();

        public LayoutRecord Read(string projectPath) => Record;
    }

    private sealed class FakeWriter : IModelViewWriter
    {
        public List<ModelViewStep> Steps { get; } = [];
        public int Added { get; set; } = 7;

        public ModelViewCreated Insert(string projectPath, ModelViewStep view)
        {
            Steps.Add(view);
            return new ModelViewCreated { ObjectId = "42", Added = Added };
        }
    }
}
