using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryStaticEntriesTests
{
    private static ApiEntry Entry(string type, string name, bool statik = true, string returns = "System.Boolean", params ApiParameter[] parameters) => new(
        new("M:" + type + "." + name, "O:" + type + "." + name, "method", name, "registered", null, [], false),
        type[..type.LastIndexOf('.')], "T:" + type, name, null, [], [new("overload", "M:" + type + "." + name, name, Parameters: parameters, ReturnType: returns, Static: statik)]);
    private static string? Reason(ApiEntry e) => ApiTryGenerator.EntryExclusion(e, e.Details[0], new Dictionary<string, string[]>(), new Dictionary<string, string[]> { ["Project"] = ["project"] });

    [Fact]
    public void StaticShapeUsesPublicCompiledDispatchWithoutTarget()
    {
        const string group = "O:Eplan.EplApi.DataModel.DocumentTypeManager.IsLogicalPageDocType";
        string[] shape = ApiTryCatalog.Shape(group, ["nPageType"])!;
        Assert.NotNull(shape);
        Assert.True(ApiTryCatalog.Static(shape[1]));
        Assert.Equal("read", ApiTryCatalog.MethodAccess(group));
    }

    [Theory]
    [InlineData("Eplan.EplApi.DataModel.ProjectSettings", "SetBoolSetting", "settings_write")]
    [InlineData("Eplan.EplApi.DataModel.ProjectSections", "SetState", "settings_write")]
    [InlineData("Eplan.EplApi.DataModel.SafetyPoint", "GetAutoLockingState", "application_or_session_state")]
    [InlineData("Eplan.EplApi.DataModel.StorableObject", "FromStringIdentifier", "unscoped_object_identifier")]
    [InlineData("Eplan.EplApi.HEServices.PartsService", "GetValue", "masterdata_or_parts_management")]
    [InlineData("Eplan.EplApi.Base.Helper", "Save", "external_or_application_effect")]
    [InlineData("Eplan.EplApi.Base.Helper", "Calculate", "static_not_project_scoped")]
    [InlineData("Eplan.EplApi.EServices.RemoteClient", "GetValue", "external_or_application_effect")]
    [InlineData("Eplan.EplApi.DataModel.Function", "Create", "lifecycle_or_creation")]
    public void StaticAndEntryEffectsStayExcluded(string type, string name, string reason) => Assert.Equal(reason, Reason(Entry(type, name)));

    [Theory]
    [InlineData("fileName", "System.String", "external_path_parameter")]
    [InlineData("url", "System.String", "url_parameter")]
    [InlineData("data", "System.IO.Stream", "stream_parameter")]
    [InlineData("database", "System.String", "database_parameter")]
    [InlineData("value", "System.Int32@", "ref_out")]
    [InlineData("value", "System.Int32[]", "array_or_params")]
    public void StaticParametersUseTheExistingSafetyRestrictions(string name, string type, string reason) => Assert.Equal(reason, Reason(Entry("Eplan.EplApi.Base.Helper", "GetValue", parameters: [new(name, type)])));

    [Fact]
    public void ProjectSettingsPathIsAnInternalSettingNameAndRemainsReadOnly()
    {
        Assert.Null(Reason(Entry("Eplan.EplApi.DataModel.ProjectSettings", "GetBoolSetting", false, parameters: [new("strSettingPath", "System.String"), new("nIdx", "System.Int32")])));
        Assert.Null(ApiTryCatalog.ValueShape("O:Eplan.EplApi.DataModel.Project.DeviceTagSettings.EnableSyntaxCheck"));
        Assert.Null(ApiTryCatalog.MethodAccess("O:Eplan.EplApi.DataModel.ProjectSettings.SetBoolSetting"));
        Assert.Contains("project", ApiTryCatalog.ViaKinds("Eplan.EplApi.DataModel.ProjectSettings", "Settings"));
        Assert.True(ApiTryCatalog.ViaKey("Eplan.EplApi.DataModel.Graphics.GraphicalLayer", "LayerTable.Layer"));
        Assert.Empty(ApiTryCatalog.ViaKinds("Eplan.EplApi.DataModel.ProjectSettings", "Project.Project.Settings"));
    }

    [Fact]
    public void PublicStaticPropertyIsReadableButNeverGeneratesASetter()
    {
        ApiEntry e = Entry("Eplan.EplApi.Base.Helper", "Value") with { Row = Entry("Eplan.EplApi.Base.Helper", "Value").Row with { Kind = "property" } };
        ApiDetail d = e.Details[0] with { Accessors = "get;set" };
        Assert.Null(ApiTryGenerator.EntryExclusion(e, d, new Dictionary<string, string[]>(), new Dictionary<string, string[]>()));
        Assert.False(ApiTryGenerator.EntryWritable(e, d));
        Assert.Equal("static_or_nonpublic", ApiTryGenerator.EntryExclusion(e, d with { Access = "protected" }, new Dictionary<string, string[]>(), new Dictionary<string, string[]>()));
        Assert.Equal("catalog:unsafe", ApiTryGenerator.EntryExclusion(e, d with { ExcludedReason = "unsafe" }, new Dictionary<string, string[]>(), new Dictionary<string, string[]>()));
        Assert.Empty(ApiTryGenerator.ObjectEntrySources("Eplan.EplApi.DataModel.Location", new([], [])));
        Assert.Null(Reason(Entry("Eplan.EplApi.Base.Helper", "UpdateProject", parameters: [new("project", "Eplan.EplApi.DataModel.Project")])));
    }

    [Fact]
    public void PropertyPathsRequirePublicCatalogGettersAndStopAfterTwoHops()
    {
        const string prefix = "Eplan.EplApi.DataModel.";
        JsonElement Type(string name, string extra = "") => JsonSerializer.Deserialize<JsonElement>("{\"id\":\"T:" + prefix + name + "\",\"kind\":\"class\"" + extra + "}");
        var types = new[] { Type("Project"), Type("Child"), Type("Leaf"), Type("Private", ",\"access\":\"internal\"") }.ToDictionary(t => t.GetProperty("id").GetString()![2..]);
        ApiEntry Property(string type, string name, string result) => Entry(prefix + type, name, false, prefix + result) with { Row = Entry(prefix + type, name).Row with { Kind = "property" }, Details = [new("overload", "P:" + prefix + type + "." + name, Parameters: [], ReturnType: prefix + result, Accessors: "get")] };
        ApiIndexData data = new([Property("Project", "Child", "Child"), Property("Child", "Leaf", "Leaf"), Property("Leaf", "Again", "Child"), Property("Project", "Hidden", "Private"), Property("Project", "NoGetter", "Child") with { Details = [new("overload", "P:hidden", Parameters: [], ReturnType: prefix + "Child", Accessors: "set")] }], []);
        var paths = ApiTryGenerator.EntryPaths(data, new Dictionary<string, string[]> { ["Project"] = ["project"] }, types);
        Assert.Equal(["Child", "Child.Leaf"], paths.Select(p => p.Path));
        Assert.False(ApiTryGenerator.PublicEntryType(prefix + "Private", types));
        types[prefix + "Nested"] = Type("Nested", ",\"outer\":\"T:" + prefix + "Private\"");
        Assert.False(ApiTryGenerator.PublicEntryType(prefix + "Nested", types));
    }
}
