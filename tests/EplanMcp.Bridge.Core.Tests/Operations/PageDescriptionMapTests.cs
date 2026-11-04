using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PageDescriptionMapTests
{
    private readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakePageReader _reader = new();
    private readonly FakePageDescriptions _texts = new();
    private readonly FakeTransactions _transactions = new();
    private static Dictionary<string, string> Languages => new() { ["en_US"] = "Panel", ["de_DE"] = "Schrank", ["zh_CN"] = "面板" };

    [Fact]
    public void CreateWritesEveryLanguageOnceAndChecksReadback()
    {
        var args = new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "=TEST+A1/1", DescriptionTranslations = Languages }] };
        var catalog = new FakeCatalog(_project);
        var writer = new FakePageWriter(_reader);
        args.Fingerprint = ((WritePlan)new PageCreatePlanOperation(catalog, _reader, writer).Execute(args)!).Fingerprint;
        new PageCreateExecuteOperation(catalog, _reader, writer, _transactions, _texts).Execute(args);
        Assert.Equal(Languages, _texts.Pages["=TEST+A1/1"].ToDictionary(t => t.Language, t => t.Text));
        Assert.Single(_texts.Writes);
        Assert.Equal(["Begin AI: eplan_page_create", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void CreateChecksAllLanguageReadbacks()
    {
        var args = new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "=TEST+A1/1", DescriptionTranslations = Languages }] };
        var catalog = new FakeCatalog(_project);
        var writer = new FakePageWriter(_reader);
        args.Fingerprint = ((WritePlan)new PageCreatePlanOperation(catalog, _reader, writer).Execute(args)!).Fingerprint;
        _texts.Tamper = entries => [.. entries.Where(t => t.Language != "de_DE")];
        var error = Assert.Throws<BridgeErrorException>(() => new PageCreateExecuteOperation(catalog, _reader, writer, _transactions, _texts).Execute(args));
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
    }

    [Fact]
    public void OriginalOverviewRequestStillUsesOnlyUiLanguage()
    {
        var args = new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "=TEST+A1/1", PageType = "Overview", Description = "Panel overview" }] };
        var catalog = new FakeCatalog(_project);
        var writer = new FakePageWriter(_reader);
        args.Fingerprint = ((WritePlan)new PageCreatePlanOperation(catalog, _reader, writer).Execute(args)!).Fingerprint;
        new PageCreateExecuteOperation(catalog, _reader, writer, _transactions, _texts).Execute(args);
        var call = Assert.Single(writer.Creates);
        Assert.Equal(("Overview", "Panel overview", null), (call.Type, call.Description, call.Language));
        Assert.Empty(_texts.Writes);
    }

    [Theory]
    [InlineData("de_DE")]
    [InlineData("zh_CN")]
    public void UpdateReadsBackEveryRequestedLanguageAndKeepsOthers(string lost)
    {
        _reader.Pages.Add(new() { Name = "/1" });
        _texts.Pages["/1"] = [new("fr_FR", "Armoire")];
        var args = UpdateArgs();
        _texts.Tamper = entries => [.. entries.Where(t => t.Language != lost)];
        var error = Assert.Throws<BridgeErrorException>(() => new PageUpdateExecuteOperation(new FakeCatalog(_project), _reader, _texts, _transactions).Execute(args));
        Assert.Equal((ExecutionState.RolledBack, lost), (error.Error.Execution, error.Error.Metadata!["language"]));
    }

    [Fact]
    public void UpdateWritesOneMultilingualValueAndPlansEachLanguage()
    {
        _reader.Pages.Add(new() { Name = "/1" });
        _texts.Pages["/1"] = [new("fr_FR", "Armoire")];
        var args = UpdateArgs();
        new PageUpdateExecuteOperation(new FakeCatalog(_project), _reader, _texts, _transactions).Execute(args);
        Assert.Single(_texts.Writes);
        Assert.Equal(4, _texts.Pages["/1"].Count);
        Assert.Equal("Armoire", _texts.Pages["/1"][0].Text);
        Assert.Equal(3, ((WritePlan)new PageUpdatePlanOperation(new FakeCatalog(_project), _reader, _texts).Execute(new PageUpdateArgs
        { Project = "Line1", Pages = [new() { Name = "/1", DescriptionTranslations = Languages.ToDictionary(t => t.Key, _ => "Changed") }] })!).Changes.Count);
        Assert.Empty(((WritePlan)new PageUpdatePlanOperation(new FakeCatalog(_project), _reader, _texts).Execute(args)!).Changes);
    }

    [Theory]
    [InlineData("en_US", 501)]
    [InlineData("xx_XX", 1)]
    [InlineData("invalid", 1)]
    public void BothToolsRejectInvalidPerLanguageValues(string language, int length)
    {
        var map = new Dictionary<string, string> { [language] = new('x', length) };
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new PageCreatePlanOperation(new FakeCatalog(_project), _reader, new FakePageWriter(_reader)).Execute(
            new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "/1", DescriptionTranslations = map }] })).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new PageUpdatePlanOperation(new FakeCatalog(_project), _reader, _texts).Execute(
            new PageUpdateArgs { Project = "Line1", Pages = [new() { Name = "/1", DescriptionTranslations = map }] })).Error.Reason);
    }

    [Fact]
    public void TwentyLanguageRequestIsNotLimitedByProjectDisplayLanguages()
    {
        var codes = new[] { "en_US", "de_DE", "zh_CN", "fr_FR", "es_ES", "it_IT", "pt_BR", "ru_RU", "pl_PL", "cs_CZ", "da_DK", "nl_NL", "fi_FI", "sv_SE", "nb_NO", "hu_HU", "ro_RO", "tr_TR", "ja_JP", "ko_KR" };
        var args = new PageCreateArgs { Project = "Line1", Pages = [new() { Name = "/1", DescriptionTranslations = codes.ToDictionary(c => c, _ => new string('x', 500)) }] };
        Assert.Single(((WritePlan)new PageCreatePlanOperation(new FakeCatalog(_project), _reader, new FakePageWriter(_reader)).Execute(args)!).Changes);
    }

    private PageUpdateArgs UpdateArgs()
    {
        var args = new PageUpdateArgs { Project = "Line1", Pages = [new() { Name = "/1", DescriptionTranslations = Languages }] };
        args.Fingerprint = ((WritePlan)new PageUpdatePlanOperation(new FakeCatalog(_project), _reader, _texts).Execute(args)!).Fingerprint;
        return args;
    }
}
