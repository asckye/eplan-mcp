using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PlacementTextLanguageTests
{
    private static readonly string[] _languages = ["de_DE", "en_US", "es_ES", "fr_FR", "nl_NL", "sv_SE", "da_DK", "ru_RU", "zh_CN", "pl_PL", "pt_BR", "cs_CZ", "it_IT", "hu_HU", "pt_PT", "ko_KR", "ja_JP", "tr_TR", "ro_RO", "zh_TW"];
    internal static Dictionary<string, string> Contents(int length) => _languages.ToDictionary(language => language, _ => new string('x', length), StringComparer.Ordinal);

    [Fact]
    public void Report1012_TwentyLanguageLengthsNoLongerExceedACombinedLimit()
    {
        // The report's single failing item; replace project text with equal-length test strings.
        string[] codes = ["de_DE", "en_US", "es_ES", "fr_FR", "nl_NL", "sv_SE", "da_DK", "ru_RU", "zh_CN", "pl_PL", "pt_BR", "cs_CZ", "it_IT", "hu_HU", "pt_PT", "ko_KR", "ja_JP", "tr_TR", "ro_RO", "zh_TW"];
        int[] lengths = [59, 44, 62, 66, 59, 47, 54, 61, 13, 79, 54, 43, 64, 64, 66, 20, 30, 43, 65, 10];
        Dictionary<string, string> contents = codes.Zip(lengths).ToDictionary(pair => pair.First, pair => new string('x', pair.Second), StringComparer.Ordinal);
        JsonElement input = JsonSerializer.SerializeToElement(new
        {
            project = "P",
            page = "P",
            layout_guard = "warn",
            dry_run = false,
            snap_to_grid = false,
            response_format = "detailed",
            items = new[] { new { kind = "text", position = (double[])[8, 127.65], height_mm = 3.5, angle = 0, alignment = "BaseLineLeft", layer = 108, pen = new { color = 0 }, contents } }
        });
        Assert.Equal(1003, contents.Values.Sum(value => value.Length));
        Assert.Empty(PlacementCreatePlanning.Validate(input));
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void TextLimit_AppliesToEachLanguage(int length, bool accepted)
    {
        JsonElement input = JsonSerializer.SerializeToElement(new { project = "P", page = "P", items = new[] { new { kind = "text", position = (double[])[8, 127.65], height_mm = 3.5, contents = Contents(length) } } });
        Assert.Equal(accepted, PlacementCreatePlanning.Validate(input).Count == 0);
        Assert.Equal(accepted, PropertyGetContractTests.Accepts(PlacementCreateContract.Input, input, PlacementCreateContract.Input));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_UnknownDisplayLanguageIsWarning(bool warn)
    {
        JsonObject input = JsonNode.Parse(PlacementCreateTests.Input("""{"kind":"text","position":[20,20],"height_mm":3.5,"contents":{"de_DE":"Eingang"}}""").GetRawText())!.AsObject();
        if (warn)
        { input["layout_guard"] = "warn"; }
        var result = await PlacementCreateTests.Preview(PlacementCreateTests.Bridge(), JsonSerializer.SerializeToElement(input), language: null);
        Assert.NotEqual(true, result.IsError);
        Assert.Contains("text visibility was not checked", PlacementCreateTests.Data(result).GetProperty("warning").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_MissingDisplayLanguageWarnsOnlyWhenRequested(bool warn)
    {
        JsonObject input = JsonNode.Parse(PlacementCreateTests.Input("""{"kind":"text","position":[20,20],"height_mm":3.5,"contents":{"de_DE":"Eingang"}}""").GetRawText())!.AsObject();
        if (warn)
        { input["layout_guard"] = "warn"; }
        var result = await PlacementCreateTests.Preview(PlacementCreateTests.Bridge(), JsonSerializer.SerializeToElement(input));
        Assert.Equal(!warn, result.IsError == true);
        if (warn)
        { Assert.Contains("project display language", PlacementCreateTests.Data(result).GetProperty("warning").GetString(), StringComparison.Ordinal); }
    }
}
