using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// project.translate.plan against a fake project (docs/tools/eplan_project_translate.md, sections 2, 4 and 5): three
/// translation languages, German as the source language, as S23 r2 set it to translate (api-notes A28).
/// </summary>
[Trait("Size", "Small")]
public sealed class ProjectTranslateOperationTests
{
    private const string Page1 = "=GAA+A1&EFS1/1";
    private const string Page2 = "=GAA+A1&EFS1/2";

    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _pages = new();
    private readonly FakeProjectLanguages _languages = new() { Settings = new LanguageSettings { Translation = "de_DE;en_US;zh_CN;", Display = "##_##;", Source = "de_DE" } };
    private readonly FakeProjectTranslator _translator;
    private readonly FakeTransactions _transactions = new();

    public ProjectTranslateOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _pages.Pages.AddRange([new PageSummary { Name = Page1, Type = "Circuit" }, new PageSummary { Name = Page2, Type = "Circuit" }]);
        _translator = new FakeProjectTranslator(_languages);
        _translator.Add(Page1, "P 1", Page1, "de_DE@Übersicht;en_US@Overview;zh_CN@概览;");
        _translator.Add(Page1, "F 1", "=GAA+A1-WE1", "de_DE@Motor;");
        _translator.Add(Page2, "F 2", "=GAA+A1-FC1", "de_DE@Schutz;en_US@Protection;");
        _translator.Add(Page2, "F 3", "=GAA+A1-FC2", "??_??@Q1;");
        _translator.Add(Page2, "F 4", "=GAA+A1-FC3", "en_US@Contactor;");
        _translator.Dictionary["Motor"] = new() { ["en_US"] = "Motor", ["zh_CN"] = "电机" };
        _transactions.Snapshot = _translator.Snapshot;
    }

    [Fact]
    public void Plan_TheProject_CountsTheTextsWithASourceTextThatMissALanguage()
    {
        WritePlan plan = Plan(Args());

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "project", "Line1", "translations"), (change.Action, change.Kind, change.Name, change.Property));
        Assert.Equal("the project: 2 of 5 texts miss a translation language; from de_DE into 2 languages", change.OldValue);
        Assert.Equal((_line1.Name, _line1.Path), (plan.Project, plan.ProjectPath));
        Assert.Equal(
            new Dictionary<string, string> { ["source"] = "de_DE", ["languages"] = "de_DE\nen_US\nzh_CN", ["textsChecked"] = "5", ["textsMissing"] = "2" },
            plan.Details);
        Assert.Equal(["Texts project"], _translator.Calls);
    }

    [Fact]
    public void Plan_Pages_ReadsOnlyThosePagesAsEplanNamesThem()
    {
        WritePlan plan = Plan(Args(pages: ["=gaa+a1&efs1/2"]));

        Assert.Equal("1 page: 1 of 3 texts miss a translation language; from de_DE into 2 languages", Assert.Single(plan.Changes).OldValue);
        Assert.Equal(["Texts " + Page2], _translator.Calls);
    }

    [Fact]
    public void Plan_NothingMissing_HasNoChange()
    {
        _translator.Stored.RemoveAll(t => t.Text.Id is "F 1" or "F 2");

        WritePlan plan = Plan(Args());

        Assert.Empty(plan.Changes);
        Assert.Equal("0", plan.Details!["textsMissing"]);
    }

    [Fact]
    public void Plan_WithoutASourceLanguage_IsInvalidAndReadsNoText()
    {
        _languages.Settings.Source = "##_##";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("Set it with eplan_language_set 'source'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Equal("de_DE\nen_US\nzh_CN", error.Metadata["candidates"]);
        Assert.Empty(_translator.Calls);
    }

    [Fact]
    public void Plan_OnlyTheSourceLanguage_IsInvalid()
    {
        _languages.Settings.Translation = "de_DE;";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("eplan_language_set 'add'", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_APageThatIsMissing_IsPageNotFoundWithEveryMissingName()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args(pages: [Page1, "=X/9", "=X/8"]))).Error;

        Assert.Equal((ErrorReason.PageNotFound, "=X/9\n=X/8"), (error.Reason, error.Metadata!["missing"]));
        Assert.Empty(_translator.Calls);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new ProjectTranslateArgs { Pages = [Page1, Page1.ToUpperInvariant(), string.Empty] })).Error;

        Assert.Equal(
            [
                "'project' is required for writes: a name or path from eplan_project_list.",
                "pages[1] repeats pages[0] (letter case does not count).",
                "pages[2] is required: a full page name as eplan_page_list returns it.",
            ],
            error.Metadata!["violations"].Split('\n'));
    }

    [Fact]
    public void Plan_MorePagesThan200_IsInvalid()
    {
        var args = Args(pages: [.. Enumerable.Range(1, 201).Select(i => "=A/" + i)]);

        Assert.Equal("'pages' must hold 1 to 200 full page names; omit it for the whole project.", Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Metadata!["violations"]);
    }

    [Fact]
    public void Plan_TheFingerprint_ChangesWithACandidateOrTheSourceButNotWithOtherTexts()
    {
        string fingerprint = Plan(Args()).Fingerprint;

        _translator.Stored.Single(t => t.Text.Id == "F 4").Text.Raw = "en_US@Relay;";
        Assert.Equal(fingerprint, Plan(Args()).Fingerprint);
        _translator.Stored.Single(t => t.Text.Id == "F 1").Text.Raw = "de_DE@Motor 2;";
        string changed = Plan(Args()).Fingerprint;
        Assert.NotEqual(fingerprint, changed);
        _languages.Settings.Source = "en_US";
        Assert.NotEqual(changed, Plan(Args()).Fingerprint);
    }

    [Theory]
    [InlineData("de_DE@Motor;en_US@Motor;", "de_DE", "Motor")]
    [InlineData("de_DE@Motor;en_US@Motor;", "en_US", "Motor")]
    [InlineData("de_DE@a;b;en_US@x;", "de_DE", "a;b")]
    [InlineData("de_DE@Zeile 1\nZeile 2;", "de_DE", "Zeile 1\nZeile 2")]
    [InlineData("de_DE@;en_US@x;", "de_DE", null)]
    [InlineData("??_??@Q1;", "de_DE", null)]
    [InlineData("en_US@Contactor;", "de_DE", null)]
    [InlineData("de_DE@Motor", "de_DE", "Motor")]
    public void TextIn_ReadsOneLanguageOfAStoredValue(string raw, string language, string? text)
    {
        Assert.Equal(text, ProjectTranslateRequest.TextIn(raw, language));
    }

    private static ProjectTranslateArgs Args(List<string>? pages = null) => new() { Project = "Line1", Pages = pages };

    private WritePlan Plan(ProjectTranslateArgs args) => (WritePlan)new ProjectTranslatePlanOperation(_catalog, _pages, _languages, _translator).Execute(args)!;
}
