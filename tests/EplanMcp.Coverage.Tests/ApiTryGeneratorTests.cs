using EplanMcp.Host.Tools;
using EplanMcp.Contracts;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryGeneratorTests
{
    [Fact]
    public void SelectionIsClosedToScalarPropertiesOnReviewedTypesAndExactMethodShape()
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

    [Theory]
    [InlineData("get;set", true)]
    [InlineData("get", false)]
    [InlineData("get;private set", false)]
    [InlineData("get;protected set", false)]
    [InlineData(null, false)]
    public void SetterSelectionRequiresAnExplicitSetter(string? accessors, bool expected) => Assert.Equal(expected,
        ApiTryGenerator.Writable(new("overload", "P:Page.Name", Accessors: accessors)));

    [Fact]
    public void ProtectedSetterOnPublicGetterIsNeverWritable() => Assert.False(ApiTryGenerator.Writable(
        new("overload", "P:Page.Name", Accessors: "get;set", SetterAccess: "protected")));

    [Fact]
    public void ReviewedTypesCoverEveryDiscoveryKind_AndInheritedFunctionTargets()
    {
        Assert.Equal(14, ApiTryGenerator.Kinds.Values.SelectMany(k => k).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["terminal", "plcterminal"], ApiTryCatalog.TargetKinds("O:Eplan.EplApi.DataModel.EObjects.Terminal.IsMainTerminal"));
        Assert.Contains("cable", ApiTryCatalog.TargetKinds("O:Eplan.EplApi.DataModel.Function.WriteProtected"));
        Assert.Contains("terminalstrip", ApiTryCatalog.TargetKinds("O:Eplan.EplApi.DataModel.Function.IsMainFunction"));
    }

    [Fact]
    public void EnumPropertiesRequireCatalogValues_AndStillHonorExclusions()
    {
        ApiDetail member = new("overload", "P:Eplan.EplApi.DataModel.Page.PageType", "Page.PageType", Parameters: [], ReturnType: "Example.Enum", Accessors: "get;set");
        ApiEntry Entry(string group, string? reason = null) => new(new(member.Id, group, "property", "Page.PageType", "registered", reason, [], false),
            "Eplan.EplApi.DataModel", "T:Eplan.EplApi.DataModel.Page", "PageType", null, [], [member]);
        ApiIndexData data = new([Entry("valid"), Entry("excluded", "unsafe")], []);
        Assert.Empty(ApiTryGenerator.Selected(data));
        Assert.Equal("valid", Assert.Single(ApiTryGenerator.Selected(data, new Dictionary<string, string[]> { ["Example.Enum"] = ["One"] })).Entry.Row.Group);
    }
}
