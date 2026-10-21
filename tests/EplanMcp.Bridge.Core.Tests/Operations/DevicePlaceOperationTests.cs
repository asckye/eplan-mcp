using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// device.place.plan against fake pages and a fake parts database (docs/tools/eplan_device_place.md, sections 2, 4, 5
/// and 7). Page /60 is new and empty, so EPLAN cannot tell its size; page /1 has the sample's size and a device
/// <c>=GAA+A1-KF1</c>.
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

    public DevicePlaceOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _placer = new Placer();
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
    }

    [Fact]
    public void Plan_IsOneDeviceChange_WithTheTagInThePagesStructure()
    {
        WritePlan plan = Plan(Args(NewPage, "-KF20"));

        Assert.Equal(("Line1", @"D:\Scratch\Line1"), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("create", "device", NewPage, "SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)"), (change.Action, change.Kind, change.Name, change.NewValue));
        Assert.Equal((Part, "1", "=GAA+A1-KF20", "false"), (plan.Details!["part"], plan.Details["variant"], plan.Details["tag"], plan.Details["pageSizeKnown"]));
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

    private static DevicePlaceArgs Args(string page, string tag, string part = Part, string? variant = null, double x = 80, double y = 160) =>
        new() { Project = "Line1", Page = page, Part = part, Variant = variant, X = x, Y = y, Tag = tag };

    private WritePlan Plan(DevicePlaceArgs args) =>
        (WritePlan)new DevicePlacePlanOperation(_catalog, _pages, _names, _parts, _placer).Execute(args)!;

    /// <summary>The pages a placement could go on.</summary>
    private sealed class Placer : IDevicePlacer
    {
        public Dictionary<string, MacroTargetPage> Pages { get; } = new(StringComparer.Ordinal);

        public MacroTargetPage? ReadPage(string projectPath, string pageName) => Pages.GetValueOrDefault(pageName);
    }
}
