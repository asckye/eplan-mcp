using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryHandleChainsTests
{
    private const string Prefix = "Eplan.EplApi.DataModel.";
    private static JsonElement Type(string name, string extra = "") => JsonSerializer.Deserialize<JsonElement>("{\"id\":\"T:" + Prefix + name + "\",\"kind\":\"class\"" + extra + "}");
    private static ApiEntry Member(string type, string name, string returns, params ApiParameter[] keys) => new(
        new("P:" + type + "." + name, "O:" + Prefix + type + "." + name, "property", name, "registered", null, [], false),
        Prefix, "T:" + Prefix + type, name, null, [], [new("overload", "P:" + type + "." + name, type + "." + name, Parameters: keys, ReturnType: returns, Accessors: "get;set")]);
    private static ApiTryGenerator.HandleModel Build(ApiEntry[] members, params JsonElement[] extra) => ApiTryGenerator.BuildHandles(new(members, []),
        [Type("Page"), .. Enumerable.Range(1, 6).Select(n => Type("T" + n)), .. extra], new() { ["Page"] = ["page"] }, new(), new());

    [Fact]
    public void FixedPointHasFourEdgeBoundAndIsIndependentOfCatalogOrder()
    {
        ApiEntry[] chain = [Member("Page", "Next", Prefix + "T1"), .. Enumerable.Range(1, 5).Select(n => Member("T" + n, "Next", Prefix + "T" + (n + 1))), Member("T4", "Name", "System.String")];
        var forward = Build(chain);
        var reverse = Build([.. chain.Reverse()]);
        Assert.Equal(forward.Reachable.Order(StringComparer.Ordinal), reverse.Reachable.Order(StringComparer.Ordinal));
        Assert.Contains(Prefix + "T4", forward.Reachable);
        Assert.DoesNotContain(Prefix + "T5", forward.Reachable);
        Assert.Contains(forward.Members, m => m.Entry.Type == "T:" + Prefix + "T4" && m.Entry.Name == "Name");
        Assert.DoesNotContain(forward.Members, m => m.Entry.Type == "T:" + Prefix + "T4" && m.Entry.Name == "Next");
    }
    [Theory]
    [InlineData("Eplan.EplApi.DataModel.T1[]")]
    [InlineData("System.Collections.Generic.List{Eplan.EplApi.DataModel.T1}")]
    [InlineData("System.Collections.Generic.IEnumerable{Eplan.EplApi.DataModel.T1}")]
    public void CollectionElementReturnsMakeChainedTargetsReachable(string returns)
    {
        var model = Build([Member("Page", "Elements", returns), Member("T1", "Next", Prefix + "T2"), Member("T2", "Name", "System.String")]);
        Assert.Contains(Prefix + "T2", model.Reachable);
        Assert.Equal(3, model.Members.Length);
    }
    [Fact]
    public void NativeCollectionsAndSimpleKeyIndexersExposeReceiversAndElements()
    {
        var model = Build([Member("Page", "Elements", Prefix + "Bag"), Member("Bag", "Item", Prefix + "T1", new ApiParameter("index", "System.Int32")), Member("T1", "Name", "System.String")],
            Type("Bag", ",\"interfaces\":[\"System.Collections.Generic.ICollection{" + Prefix + "T1}\"]"));
        Assert.Contains(Prefix + "Bag", model.Reachable);
        Assert.Contains(Prefix + "T1", model.Reachable);
        Assert.Equal(3, model.Members.Length);
    }
    [Fact]
    public void AmbiguousOverloadsNeverSeedReachability()
    {
        ApiEntry root = Member("Page", "Item", Prefix + "T1", new ApiParameter("key", "System.String"));
        root = root with { Details = [root.Details[0], root.Details[0] with { Id = "P:other", Parameters = [new("key", "System.Int32")] }] };
        Assert.Empty(Build([root]).Reachable);
    }
    [Theory]
    [InlineData("System.Object[]")]
    [InlineData("System.Collections.Generic.List{System.Object}")]
    [InlineData("System.Collections.Generic.Dictionary{System.String,Eplan.EplApi.DataModel.T1}")]
    [InlineData("Eplan.EplApi.MasterData.MDPart[]")]
    [InlineData("Eplan.EplApi.DataModel.Hidden[]")]
    public void UnsupportedOrUnsafeElementShapesStayClosed(string returns) => Assert.Empty(Build([Member("Page", "Elements", returns)], Type("Hidden", ",\"access\":\"internal\"")).Reachable);
    [Theory]
    [InlineData("Destroy")]
    [InlineData("Initialize")]
    [InlineData("Freeze")]
    [InlineData("LockObject")]
    [InlineData("Create")]
    public void LifecycleMembersNeverSeedAChain(string name)
    {
        ApiEntry root = Member("Page", name, Prefix + "T1[]");
        Assert.Empty(Build([root with { Row = root.Row with { Kind = "method" } }]).Reachable);
        if (name != "Create")
        {
            ApiEntry service = root with { Type = "T:" + Prefix + "Service", Row = root.Row with { Kind = "method" } };
            Assert.Empty(ApiTryGenerator.BuildHandles(new([service], []), [Type("Service"), Type("T1")], new(), new(), new() { [Prefix + "Service"] = new(false, null) }).Reachable);
        }
    }
    [Fact]
    public void IndexedShapesSelectByNamesAndRemainReadOnly()
    {
        const string group = "O:" + Prefix + "Graphics.GraphicalLayerTable.Layer";
        Assert.Null(ApiTryCatalog.Shape(group));
        Assert.NotNull(ApiTryCatalog.Shape(group, ["target", "id"]));
        Assert.NotNull(ApiTryCatalog.Shape(group, ["target", "name"]));
        Assert.Null(ApiTryCatalog.Shape(group, ["target", "unknown"]));
        Assert.Equal("read", ApiTryCatalog.MethodAccess(group));
        Assert.Null(ApiTryCatalog.ValueShape(group));
    }
    [Fact]
    public void SettingsAreOnlyObtainedFromAnApprovedProjectReturnAndOnlyRead()
    {
        ApiEntry Method(string name, string key) => Member("ProjectSettings", name, "System.String", new ApiParameter(key, "System.String")) with { Row = Member("ProjectSettings", name, "System.String").Row with { Kind = "method" } };
        var model = Build([Member("Page", "Settings", Prefix + "ProjectSettings"), Method("GetStringSetting", "strSettingPath"), Method("SetStringSetting", "strSettingPath"), Method("GetStringSettingFromFile", "fileName")], Type("ProjectSettings"));
        Assert.Contains(Prefix + "ProjectSettings", model.Reachable);
        Assert.Contains(model.Members, m => m.Entry.Name == "GetStringSetting");
        Assert.DoesNotContain(model.Members, m => m.Entry.Name is "SetStringSetting" or "GetStringSettingFromFile");
        Assert.Empty(Build([Method("GetStringSetting", "strSettingPath")], Type("ProjectSettings")).Reachable);
    }
    [Theory]
    [InlineData("System.Int16", "-32768", true)]
    [InlineData("System.Int16", "32767", true)]
    [InlineData("System.Int16", "32768", false)]
    [InlineData("System.UInt32", "4294967295", true)]
    [InlineData("System.UInt32", "4294967296", false)]
    [InlineData("System.UInt32", "-1", false)]
    public void AdditionalIntegerIndexKeysAreRangeChecked(string type, string value, bool valid)
    {
        Assert.Equal(valid, ApiTryCatalog.ValidValue([type], "Integer", value));
        Assert.False(ApiTryCatalog.ValidValue([type], "String", value));
        Assert.False(ApiTryCatalog.ValidValue([type], "Float", value));
    }
    [Fact]
    public void NativeInterfaceTargetsKeepTheExistingLocatorIdentity()
    {
        Assert.Contains("placeholder", ApiTryCatalog.Kinds(Prefix + "IPlaceHolder"));
        Assert.True(ApiTryCatalog.ValidValue([Prefix + "IPlaceHolder"], "locator", ""));
        string[] shape = Assert.IsType<string[]>(ApiTryCatalog.Shape("O:" + Prefix + "IPlaceHolder.NameOfRecord", ["target", "nIndex"]));
        Assert.Equal([Prefix + "IPlaceHolder"], Assert.IsType<string[]>(ApiTryCatalog.ReceiverShape(shape[1])));
        Assert.Null(ApiTryCatalog.ValueShape("O:" + Prefix + "IPlaceHolder.NameOfRecord"));
    }
    [Fact]
    public void ReturnFallbackContainsOnlyApprovedDeclaredNativeTypes()
    {
        ApiTryGenerator.HandleModel model = Build([Member("Page", "Next", Prefix + "T1"), Member("T1", "Name", "System.String"), Member("Page", "Unknown", "System.Object")]);
        string fallback = ApiTryGenerator.ChainReturnTypes(model);
        Assert.Contains("\"Page.Next\" => \"" + Prefix + "T1\"", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("T1.Name", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.Unknown", fallback, StringComparison.Ordinal);
    }
}
