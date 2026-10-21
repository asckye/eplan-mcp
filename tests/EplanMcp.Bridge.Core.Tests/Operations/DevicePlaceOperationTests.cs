using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// device.place.plan and device.place.execute against fake pages, a fake parts database and a placer that behaves as
/// S20 found <c>DeviceService.CreateDevice</c> (docs/tools/eplan_device_place.md, sections 2, 4, 5 and 7; api-notes A26):
/// a contactor's coil placed on the page and 4 contacts unplaced, all tagged <c>=+-Q1</c> until renamed. Page /60 is new
/// and empty, so EPLAN cannot tell its size; page /1 has the sample's size and a device <c>=GAA+A1-KF1</c>.
/// </summary>
[Trait("Size", "Small")]
public sealed class DevicePlaceOperationTests
{
    private const string Part = "SIE.3RH2122-1HB40";
    private const string NewPage = "=GAA+A1&EFS1/60";
    private const string SamplePage = "=GAA+A1&EFS1/1";

    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _pages = new();
    private readonly FakeDeviceRenamer _names = new();
    private readonly FakePartsDatabase _parts = new();
    private readonly Placer _placer;
    private readonly FakeTransactions _transactions = new();

    public DevicePlaceOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _placer = new Placer(_names);
        _placer.Pages[NewPage] = new MacroTargetPage { Name = NewPage, Type = "Circuit" };
        _placer.Pages[SamplePage] = new MacroTargetPage { Name = SamplePage, Type = "Circuit", Width = 420, Height = 297.02, Objects = ["1/1/1/0", "1/1/2/0"] };
        _placer.Pages["=GAA+A1&EFS2/1"] = new MacroTargetPage { Name = "=GAA+A1&EFS2/1", Type = "Overview" };
        _pages.Pages.AddRange(_placer.Pages.Keys.Select(name => new PageSummary { Name = name }));
        _names.Functions.Add(("=GAA+A1-KF1", "1/2/3/0", true, SamplePage, "KF1"));
        _parts.Content.Parts.AddRange(
        [
            new PartSearchItem { PartNumber = Part, Variant = "1", HasMacro = true },
            new PartSearchItem { PartNumber = Part, Variant = "2", HasMacro = true },
            new PartSearchItem { PartNumber = "PXC.3209510", Variant = "1", HasMacro = false },
            new PartSearchItem { PartNumber = "RIT.8601500", Variant = "3", HasMacro = true },
        ]);
        _transactions.Snapshot = () =>
        {
            var functions = _names.Functions.ToList();
            Dictionary<string, string[]> objects = _placer.Pages.ToDictionary(p => p.Key, p => p.Value.Objects.ToArray());
            return () =>
            {
                _names.Functions.Clear();
                _names.Functions.AddRange(functions);
                foreach ((string page, string[] ids) in objects)
                {
                    _placer.Pages[page].Objects = [.. ids];
                }
            };
        };
    }

    [Fact]
    public void Plan_IsOneDeviceChange_WithTheTagInThePagesStructure()
    {
        WritePlan plan = Plan(Args(NewPage, "-KF20"));

        Assert.Equal(("Line1", @"D:\Scratch\Line1"), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("create", "device", NewPage, "SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)"), (change.Action, change.Kind, change.Name, change.NewValue));
        Assert.Equal((Part, "1", "=GAA+A1-KF20", "false"), (plan.Details!["part"], plan.Details["variant"], plan.Details["tag"], plan.Details["pageSizeKnown"]));
        Assert.Empty(_placer.Places);
        Assert.Equal([null], _parts.Reads);
    }

    [Theory]
    [InlineData("=GAA+A1&EFS1/60", "=GAA+A1")]
    [InlineData("=GAA+A1/3", "=GAA+A1")]
    [InlineData("&EFS1/3", "")]
    [InlineData("==F1=GAA++P1+A1&EFS1/3", "==F1=GAA++P1+A1")]
    [InlineData("=GAA&EFS1#X1/3", "=GAA#X1")]
    public void DeviceStructureOf_IsThePagesStructureWithoutTheDocumentType(string page, string structure)
    {
        Assert.Equal(structure, DevicePlaceRequest.DeviceStructureOf(page));
    }

    [Fact]
    public void Plan_AFullTag_AVariantAndAPartInAnotherCase_AreTakenAsTheDatabaseHasThem()
    {
        WritePlan plan = Plan(Args(SamplePage, "=F+A2-KF20", part: "sie.3rh2122-1hb40", variant: "2", x: 100.5, y: 297));

        Assert.Equal("SIE.3RH2122-1HB40, variant 2 as =F+A2-KF20 at (100.5, 297)", plan.Changes[0].NewValue);
        Assert.Equal((Part, "2", "true"), (plan.Details!["part"], plan.Details["variant"], plan.Details["pageSizeKnown"]));
    }

    [Fact]
    public void Plan_WithoutAVariant_TakesThePartsOnlyOne()
    {
        WritePlan plan = Plan(Args(NewPage, "-UA1", part: "RIT.8601500"));

        Assert.Equal("3", plan.Details!["variant"]);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(new DevicePlaceArgs { Page = "", Part = " ", X = -1, Y = double.NaN, Tag = "-KF 1" }));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        string[] violations = error.Error.Metadata!["violations"].Split('\n');
        Assert.Equal(5, violations.Length);
        Assert.StartsWith("'project' is required", violations[0], StringComparison.Ordinal);
        Assert.Contains(violations, v => v.StartsWith("'tag' must not contain white space", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "'tag' is required")]
    [InlineData("KF20", "'tag' needs a device part after '-'")]
    [InlineData("=GAA+A1-", "'tag' needs a device part after '-'")]
    [InlineData("-KF20:13", "'tag' must not contain ':'")]
    public void Plan_ATagWithoutADevicePart_IsInvalid(string tag, string violation)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(Args(NewPage, tag)));

        Assert.StartsWith(violation, error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_TheWrongPageTypeAPointOffThePageAndAMissingVariant_AreReportedTogether()
    {
        _placer.Pages["=GAA+A1&EFS2/1"].Width = 420;
        _placer.Pages["=GAA+A1&EFS2/1"].Height = 297;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=GAA+A1&EFS2/1", "-KF20", variant: "9", x: 421, y: 10)));

        Assert.Equal(
            [
                "'variant' 9 does not exist; part SIE.3RH2122-1HB40 has the variants 1, 2.",
                "'page' must be a circuit page (Circuit), but '=GAA+A1&EFS2/1' is Overview.",
                "'x' and 'y' must lie on the page, 420 x 297 mm: x from 0 to 420 and y from 0 to 297.",
            ],
            error.Error.Metadata!["violations"].Split('\n'));
    }

    [Fact]
    public void Plan_APartWithoutAMacro_IsInvalid_AndPointsToMacroInsert()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(Args(NewPage, "-XD1", part: "PXC.3209510")));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Contains("eplan_macro_insert", error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_APartNotInTheDatabase_IsPartNotFound()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(Args(NewPage, "-KF20", part: "SIE.NOPE")));

        Assert.Equal((ErrorReason.PartNotFound, "SIE.NOPE"), (error.Error.Reason, error.Error.Metadata!["part"]));
        Assert.Contains("eplan_part_search", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AMissingPage_IsPageNotFound()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=GAA+A1&EFS1/999", "-KF20")));

        Assert.Equal(ErrorReason.PageNotFound, error.Error.Reason);
    }

    [Theory]
    [InlineData("-KF1")]
    [InlineData("=gaa+a1-kf1")]
    public void Plan_ATagInUse_IsAlreadyExists_IgnoringCase(string tag)
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(Args(NewPage, tag)));

        Assert.Equal((ErrorReason.AlreadyExists, "=GAA+A1-KF1"), (error.Error.Reason, error.Error.Metadata!["existing"]));
    }

    [Fact]
    public void Execute_PlacesTheDeviceAndRenamesEveryFunction_ThenCommits()
    {
        DevicePlaceArgs args = Args(NewPage, "-KF20");
        args.Fingerprint = Plan(args).Fingerprint;

        DevicePlaceData data = Execute(args);

        Assert.Equal(("Line1", NewPage, Part, "1"), (data.Project, data.Page, data.Part, data.Variant));
        Assert.Equal(("=GAA+A1-KF20", "-KF20", "Coil", 5, 4, false), (data.Tag, data.VisibleTag, data.Category, data.Functions, data.Unplaced, data.PageSizeKnown));
        Assert.Equal((NewPage, Part, "1", 80.0, 160.0, "=GAA+A1-KF20"), Assert.Single(_placer.Places));
        Assert.Equal(["Begin AI: eplan_device_place", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal(5, _names.Functions.Count(f => f.Name == "=GAA+A1-KF20"));
    }

    [Fact]
    public void Execute_WithAStaleFingerprint_IsPlanStale_AndPlacesNothing()
    {
        DevicePlaceArgs args = Args(NewPage, "-KF20");
        args.Fingerprint = Plan(args).Fingerprint;
        _placer.Pages[NewPage].Objects.Add("7/7/7/0");

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Empty(_placer.Places);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_WhenTheTagCameIntoUse_IsPlanStale()
    {
        DevicePlaceArgs args = Args(NewPage, "-KF20");
        args.Fingerprint = Plan(args).Fingerprint;
        _names.Functions.Add(("=GAA+A1-KF20", "4/4/4/0", true, SamplePage, "KF20"));

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsInvalid_AndNotStarted()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args(NewPage, "-KF20")));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    public void Execute_NothingPlacedOnThePage_IsRolledBack(int functions, int onPage)
    {
        _placer.Result = tag => new PlacedDevice { Names = [.. Enumerable.Repeat(tag, functions)], MainName = functions > 0 ? tag : string.Empty, OnPage = onPage };
        DevicePlaceArgs args = Args(NewPage, "-KF20");
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Contains("eplan_macro_insert", error.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void Execute_ATagEPLANChanged_IsRolledBack_WithWhatItGave()
    {
        // As S9 found for a cable (A13): EPLAN drops a structure part the device does not use.
        _placer.Result = tag => new PlacedDevice { Names = ["+A2-KF20", "+A2-KF20"], MainName = "+A2-KF20", OnPage = 1 };
        DevicePlaceArgs args = Args(NewPage, "=F+A2-KF20");
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Equal(("=F+A2-KF20", "+A2-KF20"), (error.Error.Metadata!["requested"], error.Error.Metadata["actual"]));
        Assert.DoesNotContain(_names.Functions, f => f.Name.EndsWith("KF20", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_AnAuxiliaryFunctionLeftWithAnotherName_IsRolledBack()
    {
        _placer.Result = tag => new PlacedDevice { Names = [tag, tag, "=+-Q1"], MainName = tag, OnPage = 1 };
        DevicePlaceArgs args = Args(NewPage, "-KF20");
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal(("=+-Q1", ExecutionState.RolledBack), (error.Error.Metadata!["actual"], error.Error.Execution));
    }

    private static DevicePlaceArgs Args(string page, string tag, string part = Part, string? variant = null, double x = 80, double y = 160) =>
        new() { Project = "Line1", Page = page, Part = part, Variant = variant, X = x, Y = y, Tag = tag };

    private WritePlan Plan(DevicePlaceArgs args) =>
        (WritePlan)new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _placer).Execute(args)!;

    private DevicePlaceData Execute(DevicePlaceArgs args) =>
        (DevicePlaceData)new DevicePlaceExecuteOperation(_catalog, _pages, _names, _parts, _placer, _transactions).Execute(args)!;

    /// <summary>
    /// <c>CreateDevice</c> as S20 saw it (A26), renamed: by default 5 functions, the coil placed on the page and 4
    /// contacts unplaced. <see cref="Result"/> replaces what is read back; the project's functions follow it.
    /// </summary>
    private sealed class Placer(FakeDeviceRenamer project) : IDevicePlacer
    {
        public Dictionary<string, MacroTargetPage> Pages { get; } = new(StringComparer.Ordinal);

        public Func<string, PlacedDevice> Result { get; set; } = tag => new PlacedDevice
        {
            Names = [tag, tag, tag, tag, tag],
            MainName = tag,
            VisibleName = "-" + tag.Split('-')[^1],
            Category = "Coil",
            Unplaced = 4,
            OnPage = 1,
        };

        public List<(string Page, string Part, string Variant, double X, double Y, string Tag)> Places { get; } = [];

        public MacroTargetPage? ReadPage(string projectPath, string pageName) => Pages.GetValueOrDefault(pageName);

        public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag)
        {
            Places.Add((pageName, part, variant, x, y, tag));
            PlacedDevice placed = Result(tag);
            project.Functions.AddRange(placed.Names.Select((name, i) => (name, "9/9/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/0", i == 0, (string?)pageName, name.Split('-')[^1])));
            Pages[pageName].Objects.Add("9/9/9/0");
            return placed;
        }
    }
}
