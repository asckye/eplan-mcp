using System.Text.Json;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class PropertyDictionaryGeneratorTests
{
    [Fact]
    public void RenderRows_SameInputInDifferentOrder_ProducesIdenticalSortedLfData()
    {
        JsonElement[] rows = Rows("""
            [
              {"object":"Project","name":"PROJ_DRAWINGNUMBER","value":"10013"},
              {"object":"Function","name":"FUNC_TEXT","value":"20011"},
              {"object":"MDFunctionTemplatePosition","name":"FUNC_TEXT","indexed":false}
            ]
            """);
        string first = PropertyDictionaryGenerator.RenderRows(rows);
        string second = PropertyDictionaryGenerator.RenderRows(rows.Reverse());
        Assert.Equal(first, second);
        Assert.DoesNotContain('\r', first);
        Assert.Contains("Function\tFUNC_TEXT\t20011\t0\treadable\n", first, StringComparison.Ordinal);
        Assert.Contains("MDFunctionTemplatePosition\tFUNC_TEXT\t\t0\tno_public_id\n", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Page", "PROPUSER_LAST_USERCODE", "3010", false, "user_metadata")]
    [InlineData("Other", "PROPUSER_NEW_FIELD", "1234", false, "user_metadata")]
    [InlineData("Other", "SOMETHING", "2000", false, "runtime_identifier")]
    [InlineData("Other", "NEW_PASSWORD", "1234", false, "restricted")]
    [InlineData("Other", "DATABASE_CONNECTIONSTRING", "1234", false, "restricted")]
    [InlineData("Other", "NEW_LICENCE", "1234", false, "restricted")]
    [InlineData("Project", "PROJ_CREATOREMAIL", "10242", false, "personal")]
    [InlineData("Other", "CONTACT_AUTHOR", "1234", false, "personal")]
    [InlineData("Other", "NEW_FILE", "1234", false, "path")]
    [InlineData("Function", "FUNC_TEXT", "20011", true, "obsolete")]
    [InlineData("Function", "FUNC_TEXT", "20011", false, "readable")]
    [InlineData("Function", "FUNC_PLCADDRESS", "20400", false, "readable")]
    [InlineData("MDFunctionTemplatePosition", "FUNC_TEXT", "", false, "no_public_id")]
    public void RenderRows_Classification_ExcludesByRuleAndReadsTheRest(string objectType, string name, string id, bool obsolete, string classification)
    {
        string input = System.Text.Json.JsonSerializer.Serialize(new[] { new { @object = objectType, name, value = id, obsolete } });
        string output = PropertyDictionaryGenerator.RenderRows(Rows(input));
        Assert.EndsWith($"{objectType}\t{name}\t{id}\t{(obsolete ? "1" : "0")}\t{classification}\n", output, StringComparison.Ordinal);
    }

    private static JsonElement[] Rows(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(e => e.Clone())];
    }
}
