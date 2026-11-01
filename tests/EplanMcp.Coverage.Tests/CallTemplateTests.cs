using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class CallTemplateTests
{
    [Theory]
    [InlineData("System.Int32@")]
    [InlineData("System.String[]@")]
    [InlineData("Eplan.EplApi.Base.PointD@")]
    public void ExplicitOutGroupEmitsOnlyInputsForTemplateSelection(string outputType)
    {
        var member = new ApiDetail("overload", "M:Page.GetTrial", "Page.GetTrial", Parameters:
            [new("points", "Eplan.EplApi.Base.PointD[]"), new("output", outputType, "out")], ReturnType: "System.Boolean");
        var entry = new ApiEntry(new(member.Id, "O:Page.GetTrial", "method", member.Signature!, "registered", null, [], false),
            "Eplan.EplApi.DataModel", "T:Eplan.EplApi.DataModel.Page", "GetTrial", null, [], [member]);
        var kinds = new Dictionary<string, string[]> { ["Page"] = ["page"] };
        var selected = Assert.Single(ApiTryGenerator.Methods(new([entry], []), new Dictionary<string, string[]>(), kinds));
        string[] shape = ApiTryGenerator.CallShape(selected.Entry, selected.Member);
        Assert.Equal(["Eplan.EplApi.DataModel.Page", "Page.GetTrial", "points"], shape);
        Assert.Equal("points", ApiTryGenerator.Names(selected.Member));
        Assert.DoesNotContain("output", shape);
        string call = ApiTryGenerator.MethodCase(entry, member, new Dictionary<string, string[]>(), kinds, new Dictionary<string, ApiTryServices.Service>());
        Assert.Contains("out arg1", call, StringComparison.Ordinal);
        Assert.DoesNotContain("parameters[\"output\"]", call, StringComparison.Ordinal);
        Assert.Contains("[\"output\"] = arg1", call, StringComparison.Ordinal);
    }
}
