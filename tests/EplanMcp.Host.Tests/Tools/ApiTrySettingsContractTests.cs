using System.Text.Json;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ApiTrySettingsContractTests
{
    [Fact]
    public void EnumerationJsonRemainsParseableAfterCleaningAndBudgetPaging()
    {
        const string group = "O:Eplan.EplApi.Base.SettingNode.GetListOfNodes";
        string value = JsonSerializer.Serialize(new { items = Enumerable.Repeat(new string('a', 200) + "\u202e", 200), total = 200, nextOffset = (int?)null });
        JsonElement parameters = JsonSerializer.SerializeToElement(new { offset = 0 });
        string result = ApiTrySettingsResult.Text(group, parameters, value)!;
        using JsonDocument page = JsonDocument.Parse(result);
        Assert.DoesNotContain("\u202e", result, StringComparison.Ordinal);
        int count = page.RootElement.GetProperty("items").GetArrayLength();
        Assert.InRange(count, 1, 199);
        Assert.Equal(count, page.RootElement.GetProperty("nextOffset").GetInt32());
        Assert.Equal(200, page.RootElement.GetProperty("total").GetInt32());
        Assert.InRange(ResultShaper.EstimateTokens(result), 1, 2000);
        Assert.Null(ApiTrySettingsResult.Text(group, parameters, null));
    }

    [Theory]
    [InlineData("Base.SettingNode.GetListOfNodes", "\"node\":\"USER\",\"offset\":0,\"limit\":200", true)]
    [InlineData("Base.SettingNode.GetListOfNodes", "\"node\":\"USER\",\"offset\":0,\"limit\":201", false)]
    [InlineData("Base.SettingNode.GetListOfNodes", "\"node\":\"USER\",\"offset\":0,\"limit\":1,\"bAbsolutPath\":true", false)]
    [InlineData("Base.SettingNode.GetListOfNodes", "\"node\":\"USER\",\"offset\":0", false)]
    [InlineData("DataModel.ProjectSettings.GetStringSetting", "\"node\":\"PROJECT\",\"strSettingPath\":\"Leaf\",\"nIdx\":0", true)]
    [InlineData("DataModel.ProjectSettings.GetStringSetting", "\"node\":\"USER\",\"strSettingPath\":\"Leaf\",\"nIdx\":0", false)]
    [InlineData("Base.SettingNode.GetStringSetting", "\"node\":\"USER\",\"strSettingPath\":\"Leaf\",\"idx\":\"0\"", false)]
    [InlineData("Base.SchemeSetting.GetCount", "\"node\":\"USER.DXF.SCHEMES\"", true)]
    [InlineData("Base.SchemeSetting.GetCount", "\"node\":\"USER.DXF.SCHEMES\",\"value\":1", false)]
    [InlineData("DataModel.Project.DeviceTagSettings.AllowedLetters", "", true)]
    [InlineData("DataModel.Project.DeviceTagSettings.AllowedLetters", "\"value\":true", false)]
    [InlineData("DataModel.Project.Settings", "", true)]
    [InlineData("DataModel.Project.Settings", "\"value\":\"PROJECT\"", false)]
    public void HostValidatesSettingsBeforeContactingBridge(string member, string fields, bool valid)
    {
        using JsonDocument json = JsonDocument.Parse("{\"target\":{\"project\":\"Scratch\",\"page\":\"\",\"type\":1,\"oid\":1,\"digest\":\"" + new string('a', 64) + "\"}" + (fields.Length > 0 ? "," + fields : "") + "}");
        Assert.Equal(valid, ApiTryTools.MethodParameters("O:Eplan.EplApi." + member, json.RootElement));
    }
}
