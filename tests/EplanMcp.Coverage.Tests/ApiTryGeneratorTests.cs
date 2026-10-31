using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryGeneratorTests
{
    [Fact]
    public void SelectionIsClosedToScalarPropertiesOnReviewedTypes()
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
        Assert.True(ObjectReadTypes.Types.Count(ObjectReadTypes.Supported) > 100);
        Assert.Contains("terminal", ApiTryCatalog.TargetKinds("O:Eplan.EplApi.DataModel.EObjects.Terminal.IsMainTerminal"));
        Assert.Contains("plcterminal", ApiTryCatalog.TargetKinds("O:Eplan.EplApi.DataModel.EObjects.Terminal.IsMainTerminal"));
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

    private static ApiEntry Method(string name, params ApiDetail[] members) => new(new(members[0].Id, "O:Page." + name, "method", name, "registered", null, [], false),
        "Eplan.EplApi.DataModel", "T:Eplan.EplApi.DataModel.Page", name, null, [], members);
    private static ApiDetail Overload(string type, string name = "value", string returns = "System.Boolean") => new("overload", "M:Page.Test(" + type + ")", Parameters: [new(name, type)], ReturnType: returns);

    [Fact]
    public void MethodsSelectExactlyOneSignaturePerNameSetWithoutUsingValueTypes()
    {
        ApiEntry entry = Method("GetTest", Overload("System.Int32"), Overload("System.String"), Overload("System.Int64", "index"));
        Assert.Equal("index", Assert.Single(ApiTryGenerator.Methods(new([entry], []))).Member.Parameters![0].Name);
        ApiEntry reordered = Method("GetOther", Overload("System.Int32") with { Parameters = [new("x", "System.Int32"), new("y", "System.Int32")] },
            Overload("System.String") with { Parameters = [new("y", "System.String"), new("x", "System.String")] });
        Assert.Empty(ApiTryGenerator.Methods(new([reordered], [])));
    }

    [Theory]
    [InlineData("Dispose", "System.Int32", "lifecycle_or_creation")]
    [InlineData("RemoveFromPage", "System.Int32", "lifecycle_or_creation")]
    [InlineData("DeleteRecord", "System.Int32", "lifecycle_or_creation")]
    [InlineData("Destroy", "System.Int32", "lifecycle_or_creation")]
    [InlineData("Initialize", "System.Int32", "lifecycle_or_creation")]
    [InlineData("init", "System.Int32", "lifecycle_or_creation")]
    [InlineData("LockObject", "System.Int32", "lifecycle_or_creation")]
    [InlineData("Freeze", "System.Int32", "lifecycle_or_creation")]
    [InlineData("EraseAll", "System.Int32", "lifecycle_or_creation")]
    [InlineData("CreateTransient", "System.Int32", "lifecycle_or_creation")]
    [InlineData("GetTest", "System.Int32@", "ref_out")]
    [InlineData("GetTest", "System.Int32[]", "array_or_params")]
    [InlineData("GetTest", "System.Action", "delegate_parameter")]
    [InlineData("GetTest", "System.Collections.Generic.List{System.String}", "generic_or_collection_parameter")]
    [InlineData("GetTest", "T", "unsupported_parameter")]
    public void MethodExclusionsAreReported(string name, string type, string reason) => Assert.Equal(reason, ApiTryGenerator.Exclusion(Method(name, Overload(type)), Overload(type), null));

    [Theory]
    [InlineData("GetTest", true)]
    [InlineData("IsTest", true)]
    [InlineData("HasTest", true)]
    [InlineData("CanTest", true)]
    [InlineData("FindTest", true)]
    [InlineData("getTest", false)]
    [InlineData("Assign", false)]
    public void MethodClassificationUsesExactCatalogName(string name, bool read) => Assert.Equal(read, ApiTryGenerator.ReadMethod(Method(name, Overload("System.Int32"))));

    [Fact]
    public void MethodsHonorVisibilityCatalogAndReturnRestrictions()
    {
        ApiDetail member = Overload("System.Int32");
        foreach (ApiDetail excluded in new[] { member with { Static = true }, member with { Access = "protected" }, member with { ExcludedReason = "unsafe" }, member with { ReturnType = "System.Object" } })
        { Assert.Empty(ApiTryGenerator.Methods(new([Method("GetTest", excluded)], []))); }
        ApiEntry entry = Method("GetTest", member);
        Assert.Empty(ApiTryGenerator.Methods(new([entry with { Row = entry.Row with { ExcludedReason = "unsafe" } }], [])));
        foreach (string type in new[] { "System.Boolean", "System.Int32", "System.Int64", "System.Double", "System.String", "Eplan.EplApi.Base.PointD", "Eplan.EplApi.DataModel.Function", "Example.Enum" })
        { Assert.Single(ApiTryGenerator.Methods(new([Method("SetTest", Overload(type, returns: "System.Void"))], []), new Dictionary<string, string[]> { ["Example.Enum"] = ["One"] })); }
    }
}
