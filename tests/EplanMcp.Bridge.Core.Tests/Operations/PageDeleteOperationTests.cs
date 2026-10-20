using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// page.delete.plan against fakes that read pages the way spike S12 found EPLAN has them (api-notes A18): exact page
/// names, the devices a deletion leaves without a main function, and every argument problem at once.
/// </summary>
[Trait("Size", "Small")]
public sealed class PageDeleteOperationTests
{
    private const string Page1 = "=GAA+A1&EFS1/1";
    private const string Page2 = "=GAA+A1&EFS1/2";
    private const string Page3 = "=GAA+A1&EFS1/3";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly FakePageReader _pages = new();
    private readonly FakePageRemover _remover;

    public PageDeleteOperationTests()
    {
        _pages.Pages.AddRange([new PageSummary { Name = Page1, Type = "Circuit" }, new PageSummary { Name = Page2, Type = "Circuit" }, new PageSummary { Name = Page3, Type = "Circuit" }]);
        _remover = new FakePageRemover(_pages);
        _remover.Placed.AddRange(
        [
            Function("+A1-WE1", main: true, Page1, "c1"), // a cable: its cores stay on page 2 (S12)
            Function("+A1-WE1", main: false, Page2, "c2"),
            Function("+A1-WE1", main: false, Page2, "c3"),
            Function("=GAA+A1-MA1", main: true, Page1, "m1"), // a second representation keeps a main function on page 3
            Function("=GAA+A1-MA1", main: false, Page2, "m2"),
            Function("=GAA+A1-MA1", main: true, Page3, "m3"),
            Function("=GAA+A1-QA1", main: true, Page1, "q1"), // everything on the deleted page
            Function("=GAA+A1-QA1", main: false, Page1, "q2"),
            Function(string.Empty, main: true, Page1, "e1"),
            Function("=GAA+A1-KF1", main: true, Page3, "k1"),
        ]);
    }

    [Fact]
    public void Plan_ListsEachPageAndTheDevicesLeftWithoutAMainFunction()
    {
        WritePlan plan = Plan(Page1);

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("delete", "page", Page1, "Circuit, 5 functions"), (change.Action, change.Kind, change.Name, change.OldValue));
        Assert.Equal((_project.Name, _project.Path), (plan.Project, plan.ProjectPath));
        Assert.Equal(Page1 + "\tCircuit\t5", plan.Details!["pages"]);
        Assert.Equal("+A1-WE1\t2\t" + Page2, plan.Details["orphaned"]);
        Assert.Equal("1", plan.Details["orphanedCount"]);
        Assert.Empty(_remover.Removed);
    }

    [Fact]
    public void Plan_CutsTheDevicesAt50AndTheirPagesAt10()
    {
        for (int i = 0; i < 55; i++)
        {
            string tag = "+A1-W" + i.ToString(CultureInfo.InvariantCulture);
            _remover.Placed.Add(Function(tag, main: true, Page1, "w" + tag));
        }

        for (int p = 10; p < 22; p++)
        {
            string page = "=GAA+A1&EFS1/" + p.ToString(CultureInfo.InvariantCulture);
            _pages.Pages.Add(new PageSummary { Name = page, Type = "Circuit" });
            _remover.Placed.Add(Function("+A1-W0", main: false, page, "core" + page));
        }

        _remover.Placed.AddRange(Enumerable.Range(1, 54).Select(i => Function("+A1-W" + i.ToString(CultureInfo.InvariantCulture), main: false, Page2, "core" + i.ToString(CultureInfo.InvariantCulture))));

        WritePlan plan = Plan(Page1);

        string[] lines = plan.Details!["orphaned"].Split('\n');
        Assert.Equal((50, "56"), (lines.Length, plan.Details["orphanedCount"]));
        Assert.Equal(12, lines.Single(l => l.StartsWith("+A1-W0\t", StringComparison.Ordinal)).Split('\t').Length);
    }

    [Fact]
    public void Plan_Fingerprint_CoversThePagesObjectsAndWhatTheOrphanedDevicesKeep()
    {
        string first = Plan(Page1).Fingerprint;
        _remover.Placed.Add(Function("=GAA+A1-KF2", main: true, Page3, "k2")); // elsewhere, unrelated
        string unrelated = Plan(Page1).Fingerprint;
        _remover.Placed.Add(Function("+A1-WE1", main: false, Page3, "c4")); // one more core left behind
        string core = Plan(Page1).Fingerprint;
        _remover.Placed.Add(Function("=GAA+A1-QA2", main: true, Page1, "q3")); // one more object on the page
        string onPage = Plan(Page1).Fingerprint;

        Assert.Equal(first, unrelated);
        Assert.Equal(4, new[] { first, core, onPage, Plan(Page1, Page3).Fingerprint }.Distinct(StringComparer.Ordinal).Count());
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void Plan_MatchesPageNamesExactly_AndNamesEveryMissingOne()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan("=gaa+a1&efs1/1", Page2, "=GAA+A1&EFS1/9")).Error;

        Assert.Equal(ErrorReason.PageNotFound, error.Reason);
        Assert.Equal("=gaa+a1&efs1/1\n=GAA+A1&EFS1/9", error.Metadata!["missing"]);
        Assert.Contains("eplan_page_list", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new PageDeletePlanOperation(new FakeCatalog(_project), _remover).Execute(new PageDeleteArgs { Pages = [Page1, " ", "=gaa+A1&EFS1/1"] })).Error;
        BridgeError none = Assert.Throws<BridgeErrorException>(() => Plan()).Error;
        BridgeError many = Assert.Throws<BridgeErrorException>(() => Plan([.. Enumerable.Range(1, 51).Select(i => "=A/" + i.ToString(CultureInfo.InvariantCulture))])).Error;

        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\npages[1] is empty; give a full page name such as =GAA+A1&EFS1/60 (eplan_page_list).\npages[2] repeats pages[0] (letter case does not count).",
            error.Metadata!["violations"]);
        Assert.All([none, many], e => Assert.Equal("'pages' must hold 1 to 50 full page names.", e.Metadata!["violations"]));
    }

    private WritePlan Plan(params string[] pages) =>
        new PageDeletePlanOperation(new FakeCatalog(_project), _remover).Execute(new PageDeleteArgs { Project = "Line1", Pages = [.. pages] }) is WritePlan plan
            ? plan
            : throw new InvalidOperationException("no plan");

    private static PlacedFunction Function(string name, bool main, string page, string id) => new() { Name = name, IsMain = main, Page = page, Id = id };
}
