using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>page.update.plan and page.update.execute against fake pages (docs/tools/eplan_page_update.md, sections 4, 5, 7).</summary>
[Trait("Size", "Small")]
public sealed class PageUpdateOperationTests
{
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _reader = new();
    private readonly FakePageDescriptions _descriptions = new();
    private readonly FakeTransactions _transactions = new();

    public PageUpdateOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _reader.Pages.AddRange([new PageSummary { Name = "=GAA+A1&EFS1/1" }, new PageSummary { Name = "=GAA+A1&EFS1/2" }, new PageSummary { Name = "=GAA+A1&EFS1/3" }]);
        _descriptions.Pages["=GAA+A1&EFS1/1"] = [new TextEntry("de_DE", "Netz"), new TextEntry("zh_CN", "电源")];
        _descriptions.Pages["=GAA+A1&EFS1/2"] = [new TextEntry("zh_CN", "控制")];
        _transactions.Snapshot = () =>
        {
            var before = _descriptions.Pages.ToDictionary(p => p.Key, p => p.Value.ToList());
            return () =>
            {
                _descriptions.Pages.Clear();
                foreach (KeyValuePair<string, List<TextEntry>> page in before)
                {
                    _descriptions.Pages[page.Key] = page.Value;
                }
            };
        };
    }

    [Fact]
    public void Plan_ListsOnlyThePagesThatChangeWithOldAndNewText()
    {
        WritePlan plan = Plan(Args(Item("=gaa+a1&efs1/1", "主回路"), Item("=GAA+A1&EFS1/2", "控制"), Item("=GAA+A1&EFS1/3", "新")));

        Assert.Equal(
            [("update", "page", "=GAA+A1&EFS1/1", "description (zh_CN)", "电源", "主回路"), ("update", "page", "=GAA+A1&EFS1/3", "description (zh_CN)", null, "新")],
            plan.Changes.Select(c => (c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue)));
        Assert.Empty(_descriptions.Writes);
    }

    [Fact]
    public void Plan_WhenNothingChanges_IsEmpty()
    {
        Assert.Empty(Plan(Args(Item("=GAA+A1&EFS1/2", "控制"))).Changes);
    }

    [Fact]
    public void Plan_ListsEveryMissingPage()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(Item("=GAA+A1&EFS1/9", "a"), Item("=GAA+A1&EFS1/1", "b"), Item("=X/1", "c")))).Error;

        Assert.Equal((ErrorReason.PageNotFound, "=GAA+A1&EFS1/9\n=X/1"), (error.Reason, error.Metadata!["missing"]));
    }

    [Fact]
    public void Plan_TwoPagesDifferingOnlyInCase_IsAmbiguous()
    {
        _reader.Pages.Add(new PageSummary { Name = "=gaa+a1&efs1/2" });

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(Item("=Gaa+A1&EFS1/2", "a")))).Error;

        Assert.Equal(ErrorReason.AmbiguousName, error.Reason);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        PageUpdateArgs args = Args(Item("=A/1", new string('x', 501)), Item("=a/1", "b"), new PageUpdateItem { Name = string.Empty, Description = null! });
        args.Language = "xx_XX";

        string[] violations = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"].Split('\n');

        Assert.Equal(5, violations.Length);
        Assert.Contains("pages[0].description must have at most 500", violations[0], StringComparison.Ordinal);
        Assert.Contains("pages[1].name repeats pages[0].name", violations[1], StringComparison.Ordinal);
        Assert.Contains("pages[2].name is required", violations[2], StringComparison.Ordinal);
        Assert.Contains("pages[2].description is required", violations[3], StringComparison.Ordinal);
        Assert.Contains("'language'", violations[4], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChangesOneLanguageAndKeepsTheOthers()
    {
        PageUpdateArgs args = Args(Item("=GAA+A1&EFS1/1", "主回路"), Item("=GAA+A1&EFS1/2", "控制"));
        args.Fingerprint = Plan(args).Fingerprint;

        PageUpdateData data = Execute(args);

        Assert.Equal(("Line1", "zh_CN", 1, null), (data.Project, data.Language, data.Unchanged, data.Warning));
        PageDescriptionChange page = Assert.Single(data.Pages);
        Assert.Equal(("=GAA+A1&EFS1/1", "主回路", "电源"), (page.Name, page.Description, page.Previous));
        (string written, string[] entries) = Assert.Single(_descriptions.Writes);
        Assert.Equal("=GAA+A1&EFS1/1", written);
        Assert.Equal(["de_DE@Netz", "zh_CN@主回路"], entries);
        Assert.Equal(["Begin AI: eplan_page_update", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_AnEmptyDescription_ClearsThatLanguageOnly()
    {
        PageUpdateArgs args = Args(Item("=GAA+A1&EFS1/1", string.Empty));
        args.Language = "zh_CN";
        args.Fingerprint = Plan(args).Fingerprint;

        PageUpdateData data = Execute(args);

        Assert.Null(Assert.Single(data.Pages).Description);
        Assert.Equal(["de_DE"], _descriptions.Pages["=GAA+A1&EFS1/1"].Select(e => e.Language));
    }

    [Fact]
    public void Execute_AfterTheDescriptionChanged_IsPlanStaleAndWritesNothing()
    {
        PageUpdateArgs args = Args(Item("=GAA+A1&EFS1/1", "主回路"));
        args.Fingerprint = Plan(args).Fingerprint;
        _descriptions.Pages["=GAA+A1&EFS1/1"].Add(new TextEntry("en_US", "Power")); // the engineer, meanwhile

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_descriptions.Writes);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_AnotherLanguageEplanLost_RollsEveryPageBack()
    {
        PageUpdateArgs args = Args(Item("=GAA+A1&EFS1/2", "新控制"), Item("=GAA+A1&EFS1/1", "主回路"));
        args.Fingerprint = Plan(args).Fingerprint;
        _descriptions.Tamper = entries => [.. entries.Where(e => e.Language != "de_DE")];

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(("=GAA+A1&EFS1/1", "de_DE", "Netz", string.Empty), (error.Metadata!["page"], error.Metadata["language"], error.Metadata["requested"], error.Metadata["actual"]));
        Assert.Equal("控制", _descriptions.Pages["=GAA+A1&EFS1/2"].Single().Text);
        Assert.Equal(2, _descriptions.Pages["=GAA+A1&EFS1/1"].Count);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(Args(Item("=GAA+A1&EFS1/1", "a")))).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    private static PageUpdateItem Item(string name, string description) => new() { Name = name, Description = description };

    private static PageUpdateArgs Args(params PageUpdateItem[] pages) => new() { Project = "Line1", Pages = [.. pages] };

    private WritePlan Plan(PageUpdateArgs args) =>
        (WritePlan)new PageUpdatePlanOperation(_catalog, _reader, _descriptions).Execute(args)!;

    private PageUpdateData Execute(PageUpdateArgs args) =>
        (PageUpdateData)new PageUpdateExecuteOperation(_catalog, _reader, _descriptions, _transactions).Execute(args)!;
}
