using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryHandlesTests
{
    private const string Prefix = "Eplan.EplApi.DataModel.";
    private static JsonElement Type(string name, string extra = "") => JsonSerializer.Deserialize<JsonElement>("{\"id\":\"T:" + Prefix + name + "\",\"kind\":\"class\"" + extra + "}");
    private static ApiEntry Entry(string type, string name, string returns, ApiParameter[]? parameters = null) => new(
        new("M:" + type + "." + name, "O:" + type + "." + name, parameters is null ? "property" : "method", name, "registered", null, [], false),
        Prefix, "T:" + Prefix + type, name, null, [], [new("overload", "M:" + type + "." + name, type + "." + name, Parameters: parameters ?? [], ReturnType: returns, Accessors: parameters is null ? "get;set" : null)]);
    private static ApiTryGenerator.HandleModel Build(params ApiEntry[] entries) => ApiTryGenerator.BuildHandles(new(entries, []),
        [Type("Page"), Type("Pin", ",\"base\":\"" + Prefix + "PinBase\""), Type("PinBase"), Type("Nested"), Type("Excluded", ",\"excluded\":{}"), Type("Hidden", ",\"access\":\"internal\""), Type("Unused")],
        new() { ["Page"] = ["page"] }, new(), new());
    [Fact]
    public void ReachabilityFollowsReturnsAndBasesAndAllowsHandleArguments()
    {
        ApiTryGenerator.HandleModel model = Build(Entry("Page", "Pin", Prefix + "Pin"), Entry("PinBase", "Nested", Prefix + "Nested"), Entry("Pin", "Name", "System.String"),
            Entry("Page", "SetPin", "System.Void", [new("pin", Prefix + "PinBase")]), Entry("Unused", "Name", "System.String"));
        Assert.Contains(Prefix + "Pin", model.Reachable);
        Assert.Contains(Prefix + "PinBase", model.Reachable);
        Assert.Contains(Prefix + "Nested", model.Reachable);
        Assert.DoesNotContain(Prefix + "Unused", model.Reachable);
        Assert.Equal(4, model.Members.Length);
    }
    [Theory]
    [InlineData("CreatePin", "Eplan.EplApi.DataModel.Pin", "method")]
    [InlineData("RemovePin", "Eplan.EplApi.DataModel.Pin", "method")]
    [InlineData("GetPin", "Eplan.EplApi.DataModel.Excluded", "method")]
    [InlineData("GetPin", "Eplan.EplApi.DataModel.Hidden", "method")]
    [InlineData("GetPin", "System.Object", "method")]
    public void UnsafeOrUnsupportedRootsDoNotMakeTypesReachable(string name, string returns, string kind)
    {
        ApiEntry entry = Entry("Page", name, returns, []);
        Assert.Equal(kind, entry.Row.Kind);
        Assert.Empty(Build(entry).Reachable);
    }
    [Theory]
    [InlineData("CreatePin")]
    [InlineData("RemovePin")]
    public void LifecycleMayReturnHandlesAlreadyReachableFromAnIndependentGetter(string name)
    {
        ApiEntry member = Entry("Page", name, Prefix + "Pin", []);
        ApiTryGenerator.HandleModel model = Build(Entry("Page", "Pin", Prefix + "Pin"), member);
        Assert.Contains(Prefix + "Pin", model.Reachable);
        Assert.Contains(model.Members, m => m.Entry.Name == name);
        Assert.False(ApiTryGenerator.ReadMethod(member));
    }
    [Fact]
    public void ExistingExclusionsAndAmbiguousNamesStayClosed()
    {
        ApiEntry root = Entry("Page", "Pin", Prefix + "Pin");
        foreach (ApiDetail detail in new[] { root.Details[0] with { Static = true }, root.Details[0] with { Access = "protected" }, root.Details[0] with { ExcludedReason = "banned_symbol" } })
        { Assert.Empty(Build(root with { Details = [detail] }).Reachable); }
        ApiEntry method = Entry("Pin", "Use", "System.Void", [new("v", "System.String")]);
        Assert.DoesNotContain(Build(root, method with { Details = [method.Details[0], method.Details[0] with { Id = "M:other", Parameters = [new("v", "System.Int32")] }] }).Members, m => m.Entry.Name == "Use");
    }
    [Fact]
    public void ReturnedHandleCollectionsAndOutValuesMakeTheirElementTypesReachable()
    {
        ApiEntry collection = Entry("Page", "GetPins", Prefix + "Pin[]", []);
        ApiTryGenerator.HandleModel model = Build(collection, Entry("Pin", "Name", "System.String"), Entry("Page", "UsePins", "System.Void", [new("pins", Prefix + "Pin[]")]));
        Assert.Contains(Prefix + "Pin", model.Reachable);
        Assert.Contains(model.Members, m => m.Entry.Name == "GetPins");
        Assert.Contains(model.Members, m => m.Entry.Name == "UsePins");
        ApiEntry output = Entry("Page", "GetPin", "System.Void", [new("pin", Prefix + "Pin@", "out")]);
        Assert.Contains(Prefix + "Pin", Build(output).Reachable);
        Assert.Equal([], ApiTryGenerator.CallShape(output, output.Details[0]).Skip(2));
    }
    [Theory]
    [InlineData("Eplan.EplApi.HEServices.Masterdata")]
    [InlineData("Eplan.EplApi.HEServices.PartsService")]
    public void ProjectMasterDataServicesRemainPending(string type) => Assert.Equal("masterdata_copy_pending", ApiTryGenerator.HandleRestriction(type));
    [Theory]
    [InlineData("Eplan.EplApi.MasterData.MDPart")]
    [InlineData("Eplan.EplApi.DataModel.MasterData.Symbol")]
    public void PartsDatabaseHandlesAreReachableUnderAdr0054(string type) => Assert.Null(ApiTryGenerator.HandleRestriction(type));
    [Fact]
    public void GeneratedShapesIncludeObjectReturnHandleTargetsAndHandleParameters()
    {
        Assert.NotNull(ApiTryCatalog.Shape("O:" + Prefix + "Connection.StartPin"));
        Assert.NotNull(ApiTryCatalog.Shape("O:" + Prefix + "Pin.Name"));
        Assert.NotNull(ApiTryCatalog.ValueShape("O:" + Prefix + "Pin.ConsumerSources"));
        Assert.True(ApiTryCatalog.ValidValue([Prefix + "PinBase"], "handle", ""));
        Assert.False(ApiTryCatalog.ValidValue(["System.String"], "handle", ""));
        Assert.False(ApiTryCatalog.ValidValue([Prefix + "Pin"], "locator", ""));
        Assert.NotNull(ApiTryCatalog.Shape("O:" + Prefix + "Project.DeviceTagSettings.AllowedLetters"));
        Assert.Null(ApiTryCatalog.ValueShape("O:" + Prefix + "Project.DeviceTagSettings.AllowedLetters"));
        Assert.True(ApiTryCatalog.HandleType("Eplan.EplApi.MasterData.MDPart"));
        Assert.True(ApiTryCatalog.Service("O:Eplan.EplApi.MasterData.MDPartsManagement.OpenDatabase"));
        Assert.Null(ApiTryCatalog.Shape("O:Eplan.EplApi.MasterData.MDPartsManagement.CreateDatabase"));
        Assert.False(ApiTryCatalog.Service("O:Eplan.EplApi.MasterData.MDPartsManagement.CreateDatabase"));
    }
    [Fact]
    public void ReturnedHelperDoesNotRequireAServiceConstructor()
    {
        ApiEntry root = Entry("Page", "Pin", Prefix + "Pin");
        ApiEntry member = Entry("Pin", "Name", "System.String");
        ApiTryGenerator.HandleModel model = ApiTryGenerator.BuildHandles(new([root, member], []), [Type("Page"), Type("Pin")],
            new() { ["Page"] = ["page"] }, new(), new() { [Prefix + "Pin"] = new(true, "no_public_default_constructor") });
        Assert.Equal(2, model.Members.Length);
    }
}
