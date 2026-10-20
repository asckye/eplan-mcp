using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// device.renumber.plan and device.renumber.execute against a fake that numbers the way spike S12 found EPLAN numbers
/// the macro page =GAA+A1&amp;EFS1/90 (api-notes A18): what counts as unnumbered, the scope, the change list, the backup
/// before the transaction, and the read-back by device. Medium: the backup goes into a real temporary folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class DeviceRenumberOperationTests : IDisposable
{
    private const string Page = "=GAA+A1&EFS1/90";
    private const string Other = "=GAA+A1&EFS1/1";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-renumber-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakeDeviceRenumberer _renumberer = new();
    private readonly FakeProjectBackup _backup;
    private readonly FakeTransactions _transactions = new();

    public DeviceRenumberOperationTests()
    {
        _pages.Pages.AddRange([new PageSummary { Name = Other }, new PageSummary { Name = Page }]);
        _backup = new FakeProjectBackup(_pages);
        _renumberer.Pages.AddRange([Other, Page]);
        string[] onPage = ["=GAA+A1-?QA1", "+A1-?XG1:?1", "+A1-?XG1:?2", "+A1-?XG2:?1", "+A1-?WG1", "=K1+A1:1.4", "+A1-XG5:?1"];
        _renumberer.Functions.AddRange(onPage.Select((name, i) => new PlacedFunction { Name = name, IsMain = true, Page = Page, Id = "p" + i }));
        _renumberer.Functions.Add(new PlacedFunction { Name = "+A1-?WE9", IsMain = true, Page = Other, Id = "o1" });
        _renumberer.Functions.Add(new PlacedFunction { Name = "+A1-XG1:1", IsMain = true, Page = Other, Id = "o2" });
        _renumberer.Numbers["+A1-?WG1"] = "+A1-WG1"; // S12: XG1 was taken, so ?XG1 became XG2
        _renumberer.Numbers["+A1-?XG1"] = "+A1-XG2";
        _renumberer.Numbers["+A1-?XG2"] = "+A1-XG3";
        _renumberer.Numbers["+A1-?WE9"] = "+A1-WE9";
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("=GAA+A1-?QA1", true)]
    [InlineData("+A1-?XG1:?1", true)]
    [InlineData("+A1-XG2:?1", false)] // a connection point's '?' is no device's (S12)
    [InlineData("=K1+A1:1.4", false)]
    [InlineData("", false)]
    public void Unnumbered_LooksAtTheTagBeforeTheConnectionPoint(string name, bool unnumbered)
    {
        Assert.Equal(unnumbered, DeviceRenumberRequest.Unnumbered(name));
    }

    [Fact]
    public void Plan_OnAPage_ListsTheDevicesWithQuestionMarks()
    {
        WritePlan plan = Plan(new DeviceRenumberArgs { Project = "Line1", Pages = [Page] });

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "devices", "Line1", "device tags", null), (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Equal("device tags containing '?' on 1 page (4 devices), scheme: EPLAN default, start 1, step 1", change.OldValue);
        Assert.Equal("=GAA+A1-?QA1\n+A1-?XG1\n+A1-?XG2\n+A1-?WG1", plan.Details!["devices"]);
        Assert.Equal(("4", "1", "true", string.Empty, "1", "1"), (plan.Details["deviceCount"], plan.Details["pageCount"], plan.Details["onlyUnnumbered"], plan.Details["scheme"], plan.Details["start"], plan.Details["step"]));
        Assert.Empty(_renumberer.Renumbers);
    }

    [Fact]
    public void Plan_AllDevicesOfTheProject_InAScheme()
    {
        WritePlan plan = Plan(new DeviceRenumberArgs { Project = "Line1", OnlyUnnumbered = false, Scheme = " Standard ", Start = 10, Step = 5 });

        Assert.Equal("all device tags in the project (8 devices), scheme: Standard, start 10, step 5", Assert.Single(plan.Changes).OldValue);
        Assert.Equal((string.Empty, "Standard"), (plan.Details!["pageCount"], plan.Details["scheme"]));
    }

    [Fact]
    public void Plan_WithNothingToNumber_HasNoChange()
    {
        _renumberer.Functions.RemoveAll(f => DeviceRenumberRequest.Unnumbered(f.Name));

        WritePlan plan = Plan(new DeviceRenumberArgs { Project = "Line1" });

        Assert.Empty(plan.Changes);
        Assert.Equal("0", plan.Details!["deviceCount"]);
    }

    [Fact]
    public void Plan_NamesEveryMissingPage_AndEveryArgumentProblemAtOnce()
    {
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Plan(new DeviceRenumberArgs { Project = "Line1", Pages = ["=gaa+a1&efs1/1", Page, "=X/1"] })).Error;
        BridgeError invalid = Assert.Throws<BridgeErrorException>(() => Plan(new DeviceRenumberArgs { Pages = [Page, "=gaa+A1&EFS1/90", ""], Start = 0, Step = 0 })).Error;
        BridgeError empty = Assert.Throws<BridgeErrorException>(() => Plan(new DeviceRenumberArgs { Project = "Line1", Pages = [] })).Error;

        Assert.Equal((ErrorReason.PageNotFound, "=gaa+a1&efs1/1\n=X/1"), (missing.Reason, missing.Metadata!["missing"]));
        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\npages[1] repeats pages[0] (letter case does not count).\npages[2] is empty; give a full page name such as =GAA+A1&EFS1/60 (eplan_page_list).\n'start' must be at least 1.\n'step' must be at least 1.",
            invalid.Metadata!["violations"]);
        Assert.Equal("'pages' must hold 1 to 50 full page names; omit it for the whole project.", empty.Metadata!["violations"]);
    }

    [Fact]
    public void Changes_AreByDevice_AndCountFunctionsWithoutAMatch()
    {
        PlacedFunction[] before = [new() { Name = "-?K1", Id = "1" }, new() { Name = "-?K1", Id = "2" }, new() { Name = "-?X1:?1", Id = "3" }, new() { Name = "-?X1:?2", Id = "4" }, new() { Name = "-A1", Id = "5" }];
        PlacedFunction[] after = [new() { Name = "-K1", Id = "1" }, new() { Name = "-K1", Id = "2" }, new() { Name = "-X1:?1", Id = "3" }, new() { Name = "-A1", Id = "5" }];

        (List<RenamedObject> changed, int unmatched) = DeviceRenumberRequest.Changes(before, after);

        Assert.Equal([("-?K1", "-K1"), ("-?X1", "-X1")], changed.Select(c => (c.From, c.To)));
        Assert.Equal(1, unmatched);
    }

    [Fact]
    public void Candidates_AreTheLastQuotedListOfTheMessage_OrNone()
    {
        Assert.Equal(
            ["Kennbuchstabe + Zähler Fluidtechnik", "Kennbuchstabe + Zähler/Zubehör Fluidtechnik", "Kennbuchstabe Zähler", "Media_code", "Standard"],
            NumberingSchemes.Candidates(FakeDeviceRenumberer.InvalidSchemeMessage));
        Assert.Empty(NumberingSchemes.Candidates("Invalid scheme."));
        Assert.Empty(NumberingSchemes.Candidates(null));
        Assert.Equal("Omit 'scheme' for EPLAN's default.", NumberingSchemes.Unknown("X", "no list", new InvalidOperationException()).Message.Split(". ")[1]);
    }

    private WritePlan Plan(DeviceRenumberArgs args) => (WritePlan)new DeviceRenumberPlanOperation(new FakeCatalog(_project), _renumberer).Execute(args)!;


}
