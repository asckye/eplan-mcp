using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class AnnotationCreateTests
{
    internal static readonly string[] AllKinds = ["hyperlink", "qr_code", "spline", "sector", "placeholder", "macro_box", "linear", "angular", "baseline", "incremental", "continued", "circle"];
    public static TheoryData<string> Kinds => new(AllKinds);

    [Theory]
    [MemberData(nameof(Kinds))]
    public void PreviewRunsSharedValidationWithoutWriting(string kind)
    {
        var world = new World();
        var result = Assert.IsType<PlacementTextCreateData>(world.Operation(planning: true).Execute(Request(kind)));
        Assert.Equal(Item(kind).Kind, Assert.Single(result.Items).Kind);
        Assert.Empty(world.Stored);
        Assert.Empty(world.Transactions.Calls);
        Assert.Equal(1, world.Validations);
    }

    [Theory]
    [InlineData("hyperlink", "empty")]
    [InlineData("qr_code", "zero")]
    [InlineData("qr_code", "nan")]
    [InlineData("spline", "one")]
    [InlineData("spline", "nan")]
    [InlineData("sector", "zero")]
    [InlineData("sector", "nan")]
    [InlineData("placeholder", "empty")]
    [InlineData("placeholder", "record")]
    [InlineData("macro_box", "empty")]
    [InlineData("linear", "one")]
    [InlineData("angular", "coincident")]
    [InlineData("angular", "end_first")]
    [InlineData("angular", "end_second")]
    [InlineData("baseline", "one")]
    [InlineData("incremental", "one")]
    [InlineData("continued", "one")]
    [InlineData("circle", "locator")]
    public void InvalidAnnotationFailsBeforeTransaction(string kind, string invalid)
    {
        var world = new World();
        PlacementTextCreateArgs args = Request(kind);
        PlacementText item = args.Items![0];
        switch (invalid)
        {
            case "empty":
                item.Link = item.Name = "";
                break;
            case "zero":
                item.SizeMm = item.RadiusMm = 0;
                break;
            case "nan":
                if (kind == "spline")
                { item.Points[0][0] = double.NaN; }
                else
                { item.SizeMm = item.StartAngle = double.NaN; }
                break;
            case "one":
                item.Points = [[20, 20]];
                break;
            case "record":
                item.Records = [""];
                break;
            case "coincident":
                item.Points[1] = item.Points[0];
                break;
            case "end_first":
                item.EndPosition = item.Points[0];
                break;
            case "end_second":
                item.EndPosition = item.Points[1];
                break;
            case "locator":
                item.Arc = null;
                break;
        }
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Operation().Execute(args)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
        Assert.Empty(world.Stored);
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("angular")]
    [InlineData("baseline")]
    [InlineData("circle")]
    public void CallerCannotProvideDimensionText(string kind)
    {
        var world = new World();
        var json = (JObject)BridgeJson.FromObject(Request(kind));
        json["items"]![0]!["dimensionText"] = new JArray("123 mm");
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Operation().ParseArgs(json)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Theory]
    [InlineData("spline", "points")]
    [InlineData("linear", "points")]
    [InlineData("linear", "endPosition")]
    public void BridgeRejectsStringCoordinatesWithoutCoercion(string kind, string field)
    {
        var world = new World();
        var json = (JObject)BridgeJson.FromObject(Request(kind));
        if (field == "points")
        { json["items"]![0]![field]![0]![0] = "20"; }
        else
        { json["items"]![0]![field]![0] = "20"; }
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Operation().ParseArgs(json)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Theory]
    [InlineData("qr_code")]
    [InlineData("spline")]
    [InlineData("sector")]
    public void KnownBoundsOutsidePageRefuseBeforeWriteEvenInWarnMode(string kind)
    {
        var world = new World();
        PlacementTextCreateArgs args = Request(kind);
        args.LayoutGuard = "warn";
        args.Items![0].Position = [999, 999];
        if (kind == "spline")
        { args.Items[0].Points = [[999, 999], [1001, 1001]]; }
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Operation().Execute(args)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    [Fact]
    public void MissingPageGeometryOnlyWarnsAndPreviewStillDoesNotWrite()
    {
        var world = new World { MissingGeometry = true };
        PlacementTextCreateData result = Assert.IsType<PlacementTextCreateData>(world.Operation(planning: true).Execute(Request("qr_code")));
        Assert.NotNull(result.Warning);
        Assert.Empty(world.Transactions.Calls);
    }

    [Fact]
    public void AnnotationKindsCannotBeSmuggledThroughPlacementCreation()
    {
        var world = new World();
        PlacementTextCreateArgs args = Request("qr_code");
        args.Annotations = false;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => world.Operation().Execute(args)).Error.Reason);
        Assert.Empty(world.Transactions.Calls);
    }

    internal static PlacementTextCreateArgs Request(string kind) => new() { Project = "Line1", Page = "P", Annotations = true, Items = [Item(kind)] };
    internal static PlacementText Item(string kind) => kind switch
    {
        "hyperlink" => new() { Kind = kind, Position = [20, 20], Link = "https://example.test", Contents = new() { ["??_??"] = "Details" } },
        "qr_code" => new() { Kind = kind, Position = [60, 20], SizeMm = 10, Text = "Payload" },
        "spline" => new() { Kind = kind, Position = [80, 20], Points = [[80, 20], [90, 30], [100, 20]] },
        "sector" => new() { Kind = kind, Position = [140, 30], RadiusMm = 10, StartAngle = 30, EndAngle = 150 },
        "placeholder" => new() { Kind = kind, Position = [180, 20], Name = "Motor", Records = ["R1"], Variables = ["Voltage"] },
        "macro_box" => new() { Kind = kind, Position = [220, 20], Name = "Motor.ema" },
        _ => new() { Kind = "dimension", DimensionType = kind, Position = [260, 20], Points = kind is "baseline" or "incremental" or "continued" ? [[260, 20], [280, 20], [300, 20]] : kind == "circle" ? [] : [[260, 20], [280, 20]], EndPosition = [280, 40], Arc = kind == "circle" ? new() { Project = "project", Page = "P", Type = 4, Oid = 104, Digest = new('a', 64) } : null }
    };

    internal sealed class World : IPlacementTextWriter, IPageDrawingReader
    {
        internal readonly List<PlacementText> Stored = [];
        internal readonly FakeTransactions Transactions = new();
        internal bool MissingGeometry;
        internal int Validations;
        internal int FailAt;
        internal Func<PlacementText, PlacementText> Readback = p => p;
        internal World()
        {
            Transactions.Snapshot = () => { PlacementText[] before = [.. Stored.Select(PlacementTextStore.Clone)]; return () => { Stored.Clear(); Stored.AddRange(before); }; };
        }
        internal PlacementTextCreateExecuteOperation Operation(bool planning = false) => new(new FakeCatalog(new OpenProject { Name = "Line1", Path = "project" }), this, Transactions, this,
            (_, _) => new() { Page = Read("project", "P"), Config = new() { MarginMm = 0 } }, planning: planning);
        public PageDrawingGeometry Read(string projectPath, string pageName) => new() { Project = "project", Page = "P", GridSize = MissingGeometry ? 0 : 1, PageSize = MissingGeometry ? null : [1000, 1000] };
        public string? Validate(string projectPath, string page, PlacementText text, bool warn = false) { Validations++; return null; }
        public PlacementText Create(string projectPath, string page, PlacementText text)
        {
            Assert.Equal("Begin AI: eplan_annotation_create", Assert.Single(Transactions.Calls));
            Stored.Add(PlacementTextStore.Clone(text));
            if (FailAt == Stored.Count)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Injected annotation failure."); }
            PlacementText actual = PlacementTextStore.Clone(text);
            if (actual.Kind == "dimension")
            { actual.DimensionText = Enumerable.Repeat("12 mm", actual.DimensionType is "baseline" or "incremental" or "continued" ? actual.Points.Length - 1 : 1).ToArray(); }
            return Readback(actual);
        }
        public List<PlacementText> CreateBatch(string projectPath, string page, IReadOnlyList<PlacementText> items) => items.Select(p => Create(projectPath, page, p)).ToList();
    }
}

[Trait("Size", "Medium")]
public sealed class AnnotationBatchTests
{
    [Theory]
    [InlineData("linear", "first")]
    [InlineData("linear", "second")]
    [InlineData("linear", "end")]
    [InlineData("angular", "end")]
    public void DimensionGeometryDeviationRollsBack(string kind, string field)
    {
        var world = new AnnotationCreateTests.World
        {
            Readback = item =>
            {
                if (field == "end")
                { item.EndPosition[0] = double.NaN; }
                else
                { item.Points[field == "first" ? 0 : 1][0] += 1; }
                return item;
            }
        };
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => world.Operation().Execute(AnnotationCreateTests.Request(kind))).Error.Execution);
        Assert.Empty(world.Stored);
    }

    [Fact]
    public void MixedBatchCommitsOnceWithAllComputedDimensionTexts()
    {
        var world = new AnnotationCreateTests.World();
        PlacementTextCreateArgs args = AnnotationCreateTests.Request("hyperlink");
        args.Items = AnnotationCreateTests.AllKinds.Select(AnnotationCreateTests.Item).ToList();
        PlacementTextCreateData result = Assert.IsType<PlacementTextCreateData>(world.Operation().Execute(args));
        Assert.Equal(12, result.Items.Count);
        Assert.Equal(["Begin AI: eplan_annotation_create", "Commit", "CloseUndo", "Dispose"], world.Transactions.Calls);
        Assert.All(result.Items.Where(i => i.Kind == "dimension"), item => Assert.All(item.DimensionText!, text => Assert.Equal("12 mm", text)));
    }

    [Fact]
    public void LaterFailureRollsBackTheWholeAnnotationBatch()
    {
        var world = new AnnotationCreateTests.World { FailAt = 2 };
        PlacementTextCreateArgs args = AnnotationCreateTests.Request("hyperlink");
        args.Items!.Add(AnnotationCreateTests.Item("qr_code"));
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => world.Operation().Execute(args)).Error.Execution);
        Assert.Empty(world.Stored);
        Assert.Equal(["Begin AI: eplan_annotation_create", "Rollback", "Dispose"], world.Transactions.Calls);
    }

    [Fact]
    public void UnknownGeometryWarnsWhileTheAnnotationTransactionCommits()
    {
        var world = new AnnotationCreateTests.World { MissingGeometry = true };
        PlacementTextCreateData result = Assert.IsType<PlacementTextCreateData>(world.Operation().Execute(AnnotationCreateTests.Request("qr_code")));
        Assert.NotNull(result.Warning);
        Assert.Single(world.Stored);
        Assert.Equal(["Begin AI: eplan_annotation_create", "Commit", "CloseUndo", "Dispose"], world.Transactions.Calls);
    }

    [Theory]
    [InlineData("qr_code")]
    [InlineData("placeholder")]
    [InlineData("linear")]
    public void ReadbackMismatchRollsBack(string kind)
    {
        var world = new AnnotationCreateTests.World
        {
            Readback = item => { if (kind == "qr_code") { item.Text = "changed"; } else if (kind == "placeholder") { item.Records = []; } else { item.DimensionText = []; } return item; }
        };
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => world.Operation().Execute(AnnotationCreateTests.Request(kind))).Error.Execution);
        Assert.Empty(world.Stored);
    }
}
