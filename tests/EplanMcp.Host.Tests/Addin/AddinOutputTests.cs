using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>The command lines EPLAN gets, and the report as text and JSON. Small.</summary>
[Trait("Size", "Small")]
public sealed class AddinOutputTests
{
    private const string Eplan = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin\EPLAN.exe";

    [Fact]
    public void CommandLines_AreTheDocumentedFormsOfEplApiModuleAction()
    {
        // api-notes B2 and eng\scripts\eplan-addin.ps1; the variant and the path inside double quotes.
        Assert.Equal(
            "/Variant:\"Pro Panel\" EplApiModuleAction /register:\"C:\\Users\\工程师\\EplanMcp\\app\\bridge\\net481\\EplanMcp.Bridge.Eplan.dll\"",
            AddinCommand.RegisterArguments("Pro Panel", @"C:\Users\工程师\EplanMcp\app\bridge\net481\EplanMcp.Bridge.Eplan.dll"));
        Assert.Equal("/Variant:\"Electric P8\" EplApiModuleAction /unregister:\"EplanMcp.Bridge.Eplan\"", AddinCommand.UnregisterArguments("Electric P8"));
    }

    [Theory]
    [InlineData(@"C:\app\bridge\net481\EplanMcp.Bridge.Eplan.dll", true)]
    [InlineData(@"c:\APP\bridge\net481\eplanmcp.bridge.eplan.DLL", true)]
    [InlineData(@"C:\app\bridge\net481", true)]
    [InlineData(@"C:\app\bridge\net481\", true)]
    [InlineData(@"D:\Code\EplanMcp\src\EplanMcp.Bridge.Eplan\bin\Release\net481\EplanMcp.Bridge.Eplan.dll", false)]
    [InlineData(@"C:\app\bridge", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Loads_IsThisDllOrItsFolder(string? reported, bool loads)
    {
        // Whether EPLAN passes OnBeforeInit the DLL or its folder is not verified (api-notes B2).
        Assert.Equal(loads, AddinCommand.Loads(new() { AddinPath = reported }, @"C:\app\bridge\net481\EplanMcp.Bridge.Eplan.dll"));
        Assert.False(AddinCommand.Loads(null, @"C:\app\bridge\net481\EplanMcp.Bridge.Eplan.dll"));
    }

    [Fact]
    public void EveryProblemAndNote_HasTextsInBothLanguagesAndItsOwnCode()
    {
        AddinReport report = new(AddinAction.Uninstall, null, null,
        [
            .. Enum.GetValues<AddinProblem>().Select(p => new AddinResult(Eplan, "2026.0.3.25421", "Pro Panel", AddinOutcome.Failed) { Problem = new(p, "detail") }),
            new(Eplan, "2026.0.3.25421", null, AddinOutcome.Skipped) { Notes = Enum.GetValues<AddinNote>() },
        ]);

        JsonArray results = JsonNode.Parse(AddinOutput.Json(report))!["results"]!.AsArray();

        string[] codes = [.. results.Select(r => r!["problem"]).OfType<JsonNode>().Select(p => p["code"]!.GetValue<string>())];
        string[] notes = [.. results[^1]!["notes"]!.AsArray().Select(n => n!["code"]!.GetValue<string>())];
        Assert.Equal(Enum.GetValues<AddinProblem>().Length, codes.Distinct().Count());
        Assert.Equal(Enum.GetValues<AddinNote>().Length, notes.Distinct().Count());
        Assert.All(results.Select(r => r!["problem"]).OfType<JsonNode>().Concat(results[^1]!["notes"]!.AsArray()!), p =>
        {
            Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", p!["zh"]!.GetValue<string>());
            Assert.DoesNotMatch(@"\p{IsCJKUnifiedIdeographs}", p!["en"]!.GetValue<string>());
        });
        Assert.Contains("detail", results[0]!["problem"]!["en"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Text_ListsEachResultWithItsProblemAndNotesInTheUiLanguage()
    {
        AddinReport report = new(AddinAction.Install, @"C:\app\bridge\net481\EplanMcp.Bridge.Eplan.dll", null,
        [
            new(Eplan, "2026.0.3.25421", "Electric P8", AddinOutcome.Registered) { ProcessId = 4000, Notes = [AddinNote.CloseEplan] },
            new(Eplan, "2026.0.3.25421", "Pro Panel", AddinOutcome.Refused) { Problem = new(AddinProblem.EplanRunning, "1234") },
        ]);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                @"插件：C:\app\bridge\net481\EplanMcp.Bridge.Eplan.dll",
                "[已注册] EPLAN 2026.0.3.25421 Electric P8（进程 4000）  " + Eplan,
                "    这个 EPLAN 没有按要求关闭，请自己关闭它；它显示了系统消息窗口时，请先关闭那个窗口",
                "[拒绝] EPLAN 2026.0.3.25421 Pro Panel  " + Eplan,
                "    EPLAN 正在运行（进程 1234）：这时执行命令会另起一个装着全部插件的 EPLAN；运行中的 EPLAN 是哪个变体判断不出来，所以有任何 EPLAN 在运行时都不执行。请先关闭所有 EPLAN，然后重新运行",
                string.Empty),
            AddinOutput.Text(report, CultureInfo.GetCultureInfo("zh-CN")));
        Assert.StartsWith(@"add-in: C:\app\bridge\net481\EplanMcp.Bridge.Eplan.dll" + Environment.NewLine + "[registered] EPLAN 2026.0.3.25421 Electric P8 (process 4000)  ", AddinOutput.Text(report, CultureInfo.GetCultureInfo("de-DE")), StringComparison.Ordinal);
    }

    [Fact]
    public void PathMismatch_WithoutAPath_SaysTheLocationIsUnknownRatherThanAQuestionMark()
    {
        AddinIssue unknown = new(AddinProblem.PathMismatch, string.Empty);

        Assert.Equal("EPLAN 加载的是不报告路径的旧版插件，它的位置未知，看不出这次的注册是否生效", AddinTexts.Problem(unknown, AddinAction.Install, zh: true));
        Assert.StartsWith("EPLAN loaded an older add-in that does not report its path", AddinTexts.Problem(unknown, AddinAction.Install, zh: false), StringComparison.Ordinal);
        Assert.Contains(@"D:\old\EplanMcp.Bridge.Eplan.dll", AddinTexts.Problem(new(AddinProblem.PathMismatch, @"D:\old\EplanMcp.Bridge.Eplan.dll"), AddinAction.Install, zh: true), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("zh-CN", "[失败] 无法读取注册记录")]
    [InlineData("en-US", "[failed] Cannot read the record of registrations")]
    public void Text_AProblemThatStoppedEverything_IsTheOnlyLine(string culture, string start)
    {
        AddinReport report = new(AddinAction.Uninstall, null, new(AddinProblem.InvalidRecord, @"C:\EplanMcp\state\addins.json: not json"), []);

        string text = AddinOutput.Text(report, CultureInfo.GetCultureInfo(culture));

        Assert.StartsWith(start, text, StringComparison.Ordinal);
        Assert.Single(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Text_NothingRecorded_SaysSo()
    {
        Assert.Equal("No registrations recorded, nothing to undo" + Environment.NewLine, AddinOutput.Text(new(AddinAction.Uninstall, null, null, []), CultureInfo.InvariantCulture));
    }
}
