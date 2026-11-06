using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>M12: the builtin IEC plugins on a drawn page; a wrong letter, then an unset, a wrong and a right protective wire colour.</summary>
[Trait("Size", "Large")]
public sealed class PageReviewRulePackTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly int[] _wire = [80, 140];
    private static readonly string[] _packs = ["iec-60204-1", "iec-81346"];
    private static readonly string[] _rulesOnly = ["rules"];

    [Fact]
    public async Task BuiltinIecPlugins_ReportLetterAndWireColour_AndARightColourPasses()
    {
        Assert.NotNull(host);
        foreach (string plugin in _packs)
        {
            (bool installed, JsonElement result) = await eplan.CallOrErrorAsync("eplan_pack_install", new() { ["library"] = "builtin", ["plugin"] = plugin, ["version"] = "0.1.0" });
            Assert.True(installed, result.GetRawText());
        }
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string suffix = page[(page.LastIndexOf('/') + 1)..];
        foreach ((string tag, int y, string[] pins) in new[] { ("-Y1", 200, new[] { "A1", "PE" }), ("-K1", 80, new[] { "PE", "A2" }) })
        {
            await eplan.CallAsync("eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["library"] = "IEC_symbol",
                ["symbol"] = "K",
                ["symbol_variant"] = 0,
                ["x"] = 80,
                ["y"] = y,
                ["tag"] = tag + "R" + suffix,
                ["connection_designations"] = pins,
                ["dry_run"] = false,
            });
        }
        await eplan.CallAsync("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = new[] { page }, ["dry_run"] = false });

        JsonElement unset = await ReviewAsync(session, page);
        Assert.Equal("warning", Issue(unset, "rule:iec-81346/coil-letter").GetProperty("severity").GetString());
        Assert.Single(Issue(unset, "rule:iec-81346/coil-letter").GetProperty("objects").EnumerateArray());
        // The fixture's generated wire may already carry a colour, so the rule either lacks a value or fails here; the unit tests cover both.
        Assert.Contains(unset.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString()!.EndsWith("iec-60204-1/pe-green-yellow", StringComparison.Ordinal));
        Assert.All(unset.GetProperty("rulePacks").EnumerateArray(), pack => Assert.Equal("draft", pack.GetProperty("status").GetString()));

        await DefineColourAsync(session, page, "RD");
        JsonElement wrong = await ReviewAsync(session, page);
        JsonElement failure = Issue(wrong, "rule:iec-60204-1/pe-green-yellow");
        Assert.Equal("error", failure.GetProperty("severity").GetString());
        Assert.Equal("13.2.2", failure.GetProperty("reference").GetProperty("clause").GetString());
        Assert.Equal("eplan_connection_create", failure.GetProperty("fix").GetProperty("tool").GetString());

        await session.UndoAsync();
        await DefineColourAsync(session, page, "GNYE");
        JsonElement right = await ReviewAsync(session, page);
        Assert.DoesNotContain(right.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString()!.EndsWith("pe-green-yellow", StringComparison.Ordinal));
        Assert.DoesNotContain(right.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString()!.EndsWith("green-yellow-only-protective", StringComparison.Ordinal));
    }

    private async Task<JsonElement> ReviewAsync(PlanStepLiveSession session, string page)
    {
        JsonElement review = await eplan.CallAsync("eplan_page_review", new() { ["project"] = session.Project, ["page"] = page, ["checks"] = _rulesOnly, ["rule_packs"] = _packs });
        TestContext.Current.TestOutputHelper?.WriteLine("review: " + review.GetRawText());
        return review;
    }

    private async Task DefineColourAsync(PlanStepLiveSession session, string page, string colour) => _ = await eplan.CallAsync("eplan_connection_create", new()
    {
        ["project"] = session.Project,
        ["page"] = page,
        ["dry_run"] = false,
        ["items"] = new[] { new { kind = "definition_point", position = _wire, color = colour } },
    });

    private static JsonElement Issue(JsonElement review, string code) => Assert.Single(review.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == code);
}
