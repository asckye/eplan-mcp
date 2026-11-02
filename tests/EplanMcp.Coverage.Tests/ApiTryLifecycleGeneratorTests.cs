using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryLifecycleGeneratorTests
{
    private const string TypeName = "Eplan.EplApi.DataModel.Graphics.Line";
    private static ApiEntry Entry(string name, ApiParameter[]? parameters = null) => new(
        new("M:" + TypeName + "." + name, "O:" + TypeName + "." + name, "method", name, "registered", null, [], false),
        "Eplan.EplApi.DataModel.Graphics", "T:" + TypeName, name, null, [],
        [new("overload", "M:" + TypeName + "." + name, TypeName + "." + name + "() : System.Void", Parameters: parameters ?? [], ReturnType: "System.Void")]);

    [Theory]
    [InlineData("Create", "creates")]
    [InlineData("CreateChild", "creates")]
    [InlineData("Remove", "removes")]
    [InlineData("RemoveFromPage", "removes")]
    [InlineData("DeleteRecord", "removes")]
    [InlineData("EraseAll", "removes")]
    public void LifecycleShapesBecomeWritesWithExplicitEffects(string name, string effect)
    {
        ApiEntry entry = Entry(name);
        if (name == "Create")
        { entry = entry with { Details = [entry.Details[0] with { Note = "Create" }] }; }
        Assert.Single(ApiTryGenerator.Methods(new([entry], [])));
        Assert.False(ApiTryGenerator.ReadMethod(entry));
        Assert.Equal(effect, ApiTryLifecycle.Effect(entry.Row.Group));
    }
    [Theory]
    [InlineData("strFileName", "System.String", "external_path_parameter")]
    [InlineData("strContureName", "System.String", "external_path_parameter")]
    [InlineData("strArticleNr", "System.String", "masterdata_or_parts_management")]
    [InlineData("variant", "Eplan.EplApi.DataModel.MasterData.SymbolVariant", "masterdata_or_parts_management")]
    [InlineData("settings", "System.String", "settings_or_scheme_parameter")]
    [InlineData("bTransient", "System.Boolean", "transient_creation")]
    public void CreationDoesNotOpenExternalResources(string name, string type, string reason)
    {
        ApiEntry entry = Entry("Create", [new(name, type)]);
        Assert.Equal(reason, ApiTryGenerator.Exclusion(entry, entry.Details[0], null));
    }
    [Theory]
    [InlineData("Destroy")]
    [InlineData("Dispose")]
    [InlineData("InitState")]
    [InlineData("Initialize")]
    [InlineData("Freeze")]
    [InlineData("LockObject")]
    public void SessionAndDestructionRemainExcluded(string name)
    {
        ApiEntry entry = Entry(name);
        Assert.Equal("lifecycle_or_creation", ApiTryGenerator.Exclusion(entry, entry.Details[0], null));
    }
    [Theory]
    [InlineData("", "", true)]
    [InlineData(",\"access\":\"protected\"", "", false)]
    [InlineData(",\"excluded\":{}", "", false)]
    [InlineData(",\"static\":true", "", false)]
    [InlineData(",\"params\":[{\"name\":\"x\",\"type\":\"System.Int32\"}]", "", false)]
    [InlineData("", ",\"modifiers\":\"abstract\"", false)]
    [InlineData("", ",\"access\":\"internal\"", false)]
    public void FactoryRequiresPublicConcreteTypeAndPublicEmptyConstructor(string ctorExtra, string typeExtra, bool expected)
    {
        JsonElement type = JsonSerializer.Deserialize<JsonElement>("{\"id\":\"T:" + TypeName + "\",\"kind\":\"class\",\"ns\":\"Eplan.EplApi.DataModel.Graphics\"" + typeExtra + "}");
        JsonElement ctor = JsonSerializer.Deserialize<JsonElement>("{\"id\":\"M:" + TypeName + ".#ctor\",\"type\":\"T:" + TypeName + "\"" + ctorExtra + "}");
        ApiEntry create = Entry("Create");
        ApiEntry[] factories = ApiTryGenerator.Factories(new([create], []), [ctor], [type]);
        Assert.Equal(expected ? 1 : 0, factories.Length);
        if (expected)
        {
            Assert.Equal("O:" + TypeName + ".Create", factories[0].Row.Group);
            Assert.Equal(create.Row, factories[0].Row);
            Assert.Equal(create.Details[0].Id, factories[0].Details[0].Id);
            Assert.Equal(create.Details[0].Signature, factories[0].Details[0].Signature);
            Assert.Equal("creates", ApiTryLifecycle.Effect(factories[0].Row.Group));
        }
    }
    [Fact]
    public void FactoryDispatchUsesCompiledConstructionAndReturnsTheCreatedReceiver()
    {
        ApiEntry entry = Entry("Create", [new("page", "Eplan.EplApi.DataModel.Page")]);
        ApiDetail member = entry.Details[0] with { Note = "Create" };
        string combined = ApiTryGenerator.FactoryDispatch(entry, member, p => p.Name);
        Assert.Contains("new global::" + TypeName + "(); created.Create(page); return created;", combined, StringComparison.Ordinal);
        Assert.Contains(JsonSerializer.Serialize(member.Signature!), combined, StringComparison.Ordinal);
        Assert.True(ApiTryGenerator.BannedFactory(entry, member, new(StringComparer.Ordinal) { "M:" + TypeName + ".#ctor" }));
        Assert.False(ApiTryGenerator.BannedFactory(entry, member with { Note = null }, new(StringComparer.Ordinal) { "M:" + TypeName + ".#ctor" }));
    }
    [Fact]
    public void ParameterizedConstructorDoesNotIntroduceAnOperationGroup()
    {
        JsonElement type = JsonSerializer.Deserialize<JsonElement>("{\"id\":\"T:" + TypeName + "\",\"kind\":\"class\"}");
        JsonElement ctor = JsonSerializer.Deserialize<JsonElement>("{\"id\":\"M:" + TypeName + ".#ctor\",\"type\":\"T:" + TypeName + "\",\"params\":[{\"name\":\"page\",\"type\":\"Eplan.EplApi.DataModel.Page\"}]}");
        Assert.Empty(ApiTryGenerator.Factories(new([Entry("Create")], []), [ctor], [type]));
        Assert.Empty(ApiTryGenerator.Factories(new([], []), [ctor], [type]));
    }
    [Fact]
    public void ResourceProjectionsDetachOnlySupportedPrimitiveIdentities()
    {
        var kinds = new Dictionary<string, string[]> { ["StorableObject"] = ["line"] };
        Assert.Contains(".IdForNetBasedConnections", ApiTryGenerator.ResourceProjection("Eplan.EplApi.DataModel.NetDefinitionPoint.NetConnectionPoint[]", kinds), StringComparison.Ordinal);
        Assert.Contains(".Id.ToString()", ApiTryGenerator.ResourceProjection("Eplan.EplApi.DataModel.Planning.PropertyConfiguration[]", kinds), StringComparison.Ordinal);
        Assert.Contains("oid = o.ObjectIdentifier", ApiTryGenerator.ResourceProjection("Eplan.EplApi.DataModel.StorableObject[]", kinds), StringComparison.Ordinal);
        Assert.Null(ApiTryGenerator.ResourceProjection("System.Object[]", kinds));
    }
    [Fact]
    public void ResourceReadbackOnlyEmitsPublicParameterlessTypedGetters()
    {
        ApiEntry names = Entry("GetRecordNames") with { Details = [Entry("GetRecordNames").Details[0] with { ReturnType = "System.Collections.Specialized.StringCollection" }] };
        ApiEntry objects = Entry("AssignedObjects") with { Row = Entry("AssignedObjects").Row with { Kind = "property" }, Details = [Entry("AssignedObjects").Details[0] with { ReturnType = "Eplan.EplApi.DataModel.StorableObject[]", Accessors = "get" }] };
        var kinds = new Dictionary<string, string[]> { ["Graphics.Line"] = ["line"], ["StorableObject"] = ["line"] };
        string output = ApiTryGenerator.LifecycleReadback(new([names, objects], []), kinds);
        Assert.Contains(".GetRecordNames().Cast<string>().ToArray()", output, StringComparison.Ordinal);
        Assert.Contains(".AssignedObjects.Select(o => new", output, StringComparison.Ordinal);
        foreach (ApiDetail detail in new[] { names.Details[0] with { Access = "protected" }, names.Details[0] with { Static = true }, names.Details[0] with { ExcludedReason = "unsafe" }, names.Details[0] with { Parameters = [new("x", "System.Int32")] } })
        { Assert.DoesNotContain("GetRecordNames()", ApiTryGenerator.LifecycleReadback(new([names with { Details = [detail] }], []), kinds), StringComparison.Ordinal); }
    }
    [Fact]
    public void GeneratedFactoryUsesExistingGroupWithoutReceiverAndExactPageAndPointShapes()
    {
        const string group = "O:" + TypeName + ".Create";
        Assert.Equal("write", ApiTryCatalog.MethodAccess(group));
        string[] shape = ApiTryCatalog.Shape(group, ["page", "pntStart", "pntEnd"])!;
        Assert.Contains(ApiTryCatalog.Methods(group), method => method.SequenceEqual(shape));
        Assert.Null(ApiTryCatalog.ReceiverShape(shape[1]));
        Assert.True(ApiTryCatalog.Factory(shape[1]));
        Assert.StartsWith(TypeName + ".Create(", shape[1], StringComparison.Ordinal);
        Assert.Equal("Eplan.EplApi.DataModel.Page", ApiTryCatalog.ParameterShape(shape[1], "page")![0]);
        Assert.Equal("Eplan.EplApi.Base.PointD", ApiTryCatalog.ParameterShape(shape[1], "pntStart")![0]);
        Assert.Null(ApiTryCatalog.Shape(group, ["unknown"]));
    }
    [Theory]
    [InlineData("E3D.HandleMate", "mate")]
    [InlineData("Graphics.GraphicalLayer", "table", "name", "description")]
    public void ExistingHandleFactoriesAlsoOmitReceiverAndReturnThroughTheirProducers(string type, params string[] names)
    {
        string native = "Eplan.EplApi.DataModel." + type, group = "O:" + native + ".Create";
        string[] shape = Assert.IsType<string[]>(ApiTryCatalog.Shape(group, names));
        Assert.True(ApiTryCatalog.HandleType(native));
        Assert.True(ApiTryCatalog.Factory(shape[1]));
        Assert.Null(ApiTryCatalog.ReceiverShape(shape[1]));
        Assert.Contains(group, ApiTryCatalog.Producers(native));
        Assert.Equal("write", ApiTryCatalog.MethodAccess(group));
    }
}
