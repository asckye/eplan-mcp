using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Host.Tests.Tools;

public sealed partial class ApiTryContractTests
{
    [Fact]
    public void IndexedOverloadKeepsTheExistingParameterlessPropertyTemplateAndAccess()
    {
        const string Group = "O:Eplan.EplApi.DataModel.ArticlePropertyList.ARTICLE_SUITABLE_FOR_PCF_HCS_FIBRE_230_uM";
        var entry = new ApiEntry(new("", Group, "property", "", "registered", null, [], false), "Eplan.EplApi.DataModel", "T:Eplan.EplApi.DataModel.ArticlePropertyList", "ARTICLE_SUITABLE_FOR_PCF_HCS_FIBRE_230_uM", null, [], []);
        var templates = ApiCallTemplates.For(entry);
        Assert.Null(ApiTryCatalog.MethodAccess(Group));
        Assert.Contains(templates, t => t["example"]!["parameters"]!["index"] is null);
        Assert.Contains(templates, t => t["example"]!["parameters"]!["index"] is not null);
        foreach (var template in templates)
        {
            JsonElement parameters = JsonSerializer.SerializeToElement(template["example"]!["parameters"]);
            Assert.Equal(template["signature"]!.GetValue<string>(), ApiTryCatalog.Shape(Group, parameters.EnumerateObject().Select(p => p.Name))![1]);
            Assert.True(ApiTryTools.MethodParameters(Group, parameters));
        }
    }
    [Theory]
    [InlineData("\"id\":0", true)]
    [InlineData("\"name\":\"A\"", true)]
    [InlineData("\"id\":\"0\"", false)]
    [InlineData("\"id\":-32769", false)]
    [InlineData("\"id\":0,\"value\":true", false)]
    [InlineData("\"id\":0,\"extra\":1", false)]
    public void HostChecksIndexedKeysAgainstTheSameGeneratedSignature(string key, bool valid)
    {
        JsonElement p = JsonDocument.Parse("{\"target\":{\"handle\":\"h:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"type\":\"Eplan.EplApi.DataModel.Graphics.GraphicalLayerTable\"}," + key + "}").RootElement;
        Assert.Equal(valid, ApiTryTools.MethodParameters("O:Eplan.EplApi.DataModel.Graphics.GraphicalLayerTable.Layer", p));
    }
}
