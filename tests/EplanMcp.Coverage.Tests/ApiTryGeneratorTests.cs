using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryGeneratorTests
{
    [Fact]
    public void SelectionIsClosedToScalarReadsOnReviewedTypesAndExactMethodShape()
    {
        ApiDetail getter = new("overload", "P:Eplan.EplApi.DataModel.Page.Name", "Page.Name", Parameters: [], ReturnType: "System.String", Accessors: "get;set");
        ApiEntry Entry(string group, ApiDetail member, string? excluded = null, string type = "T:Eplan.EplApi.DataModel.Page") =>
            new(new(member.Id, group, "property", member.Signature!, excluded is null ? "registered" : "excluded", excluded, [], false), "Eplan.EplApi.DataModel", type, "Name", null, [], [member]);
        ApiIndexData data = new([
            Entry("good", getter), Entry("excluded", getter, "unsafe"),
            Entry("overload-excluded", getter with { ExcludedReason = "internal" }),
            Entry("static", getter with { Static = true }),
            Entry("setter-only", getter with { Accessors = "set" }),
            Entry("object", getter with { ReturnType = "System.Object" }),
            Entry("indexer", getter with { Parameters = [new("index", "System.Int32")] }),
            Entry("unreviewed-type", getter, type: "T:System.IO.File"),
        ], []);
        Assert.Equal("good", Assert.Single(ApiTryGenerator.Selected(data)).Entry.Row.Group);
    }
}
