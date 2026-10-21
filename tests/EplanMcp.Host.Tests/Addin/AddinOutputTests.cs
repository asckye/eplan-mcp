using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>The report as text and JSON. Small.</summary>
[Trait("Size", "Small")]
public sealed class AddinOutputTests
{
    private const string Eplan = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin\EPLAN.exe";

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
                "    这个 EPLAN 没有按要求关闭，请自己关闭它",
                "[拒绝] EPLAN 2026.0.3.25421 Pro Panel  " + Eplan,
                "    Pro Panel 的 EPLAN 正在运行，或有无法判断变体的 EPLAN 在运行（进程 1234）：这时执行命令会另起一个装着全部插件的 EPLAN。请先关闭它，然后重新运行",
                string.Empty),
            AddinOutput.Text(report, CultureInfo.GetCultureInfo("zh-CN")));
        Assert.StartsWith(@"add-in: C:\app\bridge\net481\EplanMcp.Bridge.Eplan.dll" + Environment.NewLine + "[registered] EPLAN 2026.0.3.25421 Electric P8 (process 4000)  ", AddinOutput.Text(report, CultureInfo.GetCultureInfo("de-DE")), StringComparison.Ordinal);
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
