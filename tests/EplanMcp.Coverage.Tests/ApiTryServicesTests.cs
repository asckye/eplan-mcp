using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryServicesTests
{
    private const string Prefix = "Eplan.EplApi.HEServices.";
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static JsonElement Type(string name, string tail = "") => Json("{\"id\":\"T:" + Prefix + name + "\",\"kind\":\"class\",\"ns\":\"Eplan.EplApi.HEServices\"" + tail + "}");
    private static JsonElement Constructor(string name, string tail = "") => Json("{\"type\":\"T:" + Prefix + name + "\",\"kind\":\"constructor\"" + tail + "}");
    private static ApiEntry Entry(string method, string type = "Edit", ApiParameter[]? parameters = null, string returns = "System.Void") => new(
        new("M:" + Prefix + type + "." + method, "O:" + Prefix + type + "." + method, "method", method, "registered", null, [], false),
        "Eplan.EplApi.HEServices", "T:" + Prefix + type, method, null, [],
        [new("overload", "M:" + Prefix + type + "." + method, method, Parameters: parameters ?? [], ReturnType: returns)]);
    private static string? Exclusion(ApiEntry e) => ApiTryGenerator.Exclusion(e, e.Details[0], null, services:
        new Dictionary<string, ApiTryServices.Service> { [e.Type[2..]] = new(true, null) });

    [Fact]
    public void ConstructionRequiresPublicConcreteTypeAndPublicDefaultConstructor()
    {
        var services = ApiTryServices.Build([
            Type("Edit", ",\"interfaces\":[\"System.IDisposable\"]"),
            Type("Derived", ",\"base\":\"" + Prefix + "Edit\""),
            Type("Abstract", ",\"modifiers\":\"abstract\""),
            Type("Protected"), Type("Arguments"), Type("Missing"), Type("Static"),
            Type("Internal", ",\"access\":\"internal\""),
            Type("Obsolete", ",\"excluded\":{\"reason\":\"deprecated\"}"),
            Type("ModelObject", ",\"base\":\"Eplan.EplApi.DataModel.StorableObject\""),
            Type("Edit.Nested", ",\"outer\":\"T:" + Prefix + "Internal\"")
        ], [Constructor("Edit"), Constructor("Derived"), Constructor("Abstract"), Constructor("Internal"), Constructor("Obsolete"),
            Constructor("Protected", ",\"access\":\"protected\""), Constructor("Arguments", ",\"params\":[{\"name\":\"x\",\"type\":\"System.Int32\"}]"),
            Constructor("Static", ",\"static\":true"), Constructor("ModelObject"), Constructor("Edit.Nested")]);
        Assert.Null(services[Prefix + "Edit"].Reason);
        Assert.True(services[Prefix + "Derived"].Disposable);
        Assert.Equal("abstract_service", services[Prefix + "Abstract"].Reason);
        foreach (string name in new[] { "Protected", "Arguments", "Missing", "Static" })
        { Assert.Equal("no_public_default_constructor", services[Prefix + name].Reason); }
        foreach (string name in new[] { "Internal", "Obsolete", "Edit.Nested" })
        { Assert.Equal("service_type_excluded", services[Prefix + name].Reason); }
        Assert.DoesNotContain(Prefix + "ModelObject", services.Keys);
    }

    [Theory]
    [InlineData("strFileName", "System.String", "external_path_parameter")]
    [InlineData("strProjectName", "System.String", "external_path_parameter")]
    [InlineData("directory", "System.String", "external_path_parameter")]
    [InlineData("strTargetDir", "System.String", "external_path_parameter")]
    [InlineData("input", "System.IO.FileInfo", "external_path_parameter")]
    [InlineData("input", "System.IO.Stream", "stream_parameter")]
    [InlineData("database", "System.String", "database_parameter")]
    [InlineData("input", "Eplan.EplApi.MasterData.MDPart", "masterdata_or_parts_management")]
    [InlineData("strSchemaName", "System.String", "settings_or_scheme_parameter")]
    [InlineData("input", "Eplan.EplApi.Base.Settings", "settings_or_scheme_parameter")]
    [InlineData("printer", "System.String", "printer_parameter")]
    [InlineData("destination", "System.Uri", "url_parameter")]
    public void ExternalResourcesAreExcludedBeforeParameterShapeSelection(string name, string type, string reason) =>
        Assert.Equal(reason, Exclusion(Entry("GetValue", parameters: [new(name, type)])));

    [Theory]
    [InlineData("Print", "Project", "external_destination")]
    [InlineData("Export", "Project", "external_destination")]
    [InlineData("Import", "Project", "external_destination")]
    [InlineData("Backup", "Project", "external_destination")]
    [InlineData("Restore", "Project", "external_destination")]
    [InlineData("Masterdata", "GetValue", "masterdata_or_parts_management")]
    [InlineData("PartsService", "GetValue", "masterdata_or_parts_management")]
    [InlineData("Settings", "GetValue", "settings_or_scheme_parameter")]
    [InlineData("License", "HasOption", "licensing_or_application_exit")]
    [InlineData("Application", "Exit", "licensing_or_application_exit")]
    [InlineData("Revision", "CreateComparisonProject", "external_destination")]
    public void SharedDataAndImplicitDestinationsAreExcluded(string type, string name, string reason) => Assert.Equal(reason, Exclusion(Entry(name, type)));

    [Fact]
    public void SupportedServiceWritesIncludeCreationAndRemovalButNeverComplexReturns()
    {
        foreach (string method in new[] { "Create", "Delete", "Remove", "BringToFront" })
        { Assert.Null(Exclusion(Entry(method, parameters: [new("page", "Eplan.EplApi.DataModel.Page")]))); }
        Assert.Equal("unsupported_return", Exclusion(Entry("GetValue", returns: "Eplan.EplApi.Base.PointD")));
        Assert.Equal("unsupported_return", Exclusion(Entry("GetValue", returns: "System.Object")));
        Assert.Equal("static_or_nonpublic", ApiTryGenerator.Exclusion(Entry("GetValue"), Entry("GetValue").Details[0] with { Static = true }, null));
        Assert.Equal("owns_transaction", Exclusion(Entry("EvaluateAndSetAllNamesTXN", "NameService")));
        Assert.Equal("external_path_parameter", Exclusion(Entry("ImportData", "PlcService", [new("strFullName", "System.String")])));
    }

    [Fact]
    public void ReadPrefixesWithSideEffectParametersAreConservativelyWrites()
    {
        Assert.True(ApiTryGenerator.ReadMethod(Entry("GetSelectedObject", parameters: [new("bFirst", "System.Boolean")])));
        foreach (string name in new[] { "bUseSelDlg", "bShowOutput", "bUpdate", "bLock", "bCreate" })
        { Assert.False(ApiTryGenerator.ReadMethod(Entry("GetValue", parameters: [new(name, "System.Boolean")]))); }
    }

    [Fact]
    public void GeneratedServiceShapesUseProjectAnchorAndTypedObjectArguments()
    {
        const string read = "O:Eplan.EplApi.HEServices.SelectionSet.GetSelectedObject";
        const string write = "O:Eplan.EplApi.HEServices.Edit.BringToFront";
        Assert.True(ApiTryCatalog.Service(read));
        Assert.Equal("read", ApiTryCatalog.MethodAccess(read));
        Assert.Equal("write", ApiTryCatalog.MethodAccess(write));
        Assert.Equal(["project"], ApiTryCatalog.TargetKinds(write));
        string[] shape = ApiTryCatalog.Shape(write, ["target", "oPlacementToMove"])!;
        Assert.Equal("Eplan.EplApi.DataModel.Placement", ApiTryCatalog.ParameterShape(shape[1], "oPlacementToMove")![0]);
        Assert.Null(ApiTryCatalog.MethodAccess("O:Eplan.EplApi.HEServices.Masterdata.Update"));
    }
}
