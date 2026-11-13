using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryFilesTests
{
    private static ApiEntry Entry(string method, string parameter = "strFileName", string type = "DeviceService", string? summary = null) => new(
        new("M:Test", "O:Test", "method", method, "registered", null, [], false), "Eplan.EplApi.HEServices", "T:Eplan.EplApi.HEServices." + type,
        method, summary, [], [new("overload", "M:Test", method, Summary: summary, Parameters: [new(parameter, "System.String")], ReturnType: "System.Void")]);
    private static string? Exclusion(ApiEntry e) => ApiTryGenerator.Exclusion(e, e.Details[0], null,
        services: new Dictionary<string, ApiTryServices.Service> { [e.Type[2..]] = new(false, null) });

    [Theory]
    [InlineData("ImportData", "strFileName", "input")]
    [InlineData("ReadFile", "strFileName", "input")]
    [InlineData("LoadFile", "strFileName", "input")]
    [InlineData("ExportData", "strFileName", "output")]
    [InlineData("SaveFile", "strFileName", "output")]
    [InlineData("WriteFile", "strFileName", "output")]
    [InlineData("Convert", "strInputFileName", "input")]
    [InlineData("Convert", "strImportFilePath", "input")]
    [InlineData("Convert", "sourceFile", "input")]
    [InlineData("Convert", "outputFile", "output")]
    [InlineData("Convert", "exportFile", "output")]
    [InlineData("Convert", "targetFile", "output")]
    [InlineData("Convert", "destinationFile", "output")]
    [InlineData("Convert", "strFileName", null)]
    [InlineData("ImportExport", "strFileName", null)]
    [InlineData("Convert", "inputOutputFile", null)]
    [InlineData("ExportData", "strDestinationPath", "output_directory")]
    [InlineData("ExportData", "strTargetDirectory", "output_directory")]
    [InlineData("ImportData", "strTargetDirectory", null)]
    [InlineData("ImportData", "strFullLinkFileName", null)]
    [InlineData("ImportData", "strProjectPath", null)]
    [InlineData("ImportData", "templateFile", null)]
    [InlineData("ImportData", "directory", null)]
    public void DirectionRequiresAnUnambiguousFile(string method, string parameter, string? expected)
    {
        ApiEntry e = Entry(method, parameter);
        Assert.Equal(expected, ApiTryGenerator.FileDirection(e, e.Details[0], e.Details[0].Parameters![0]));
        Assert.Equal(expected is null ? "external_path_parameter" : null, Exclusion(e));
    }

    [Theory]
    [InlineData("Imports data from a file.", "input")]
    [InlineData("Exports project data to a file.", "output")]
    [InlineData("Imports and exports files.", null)]
    [InlineData("Processes a file.", null)]
    public void DocumentationSuppliesDirectionWithoutGuessing(string summary, string? expected)
    {
        ApiEntry e = Entry("Process", summary: summary);
        Assert.Equal(expected, ApiTryGenerator.FileDirection(e, e.Details[0], e.Details[0].Parameters![0]));
    }

    [Theory]
    [InlineData("Print", "ExportData")]
    [InlineData("Mailer", "SendFile")]
    [InlineData("Masterdata", "ImportData")]
    [InlineData("PartsService", "ImportData")]
    [InlineData("Backup", "SaveFile")]
    [InlineData("Restore", "LoadFile")]
    [InlineData("Import", "ImportData")]
    public void OtherExternalSideEffectsKeepTheirExclusions(string type, string method) => Assert.NotNull(Exclusion(Entry(method, type: type)));

    [Theory]
    [InlineData("Export", "ExportData", "strFileName")]
    [InlineData("Export3D", "ProjectToIFC", "strTargetDirectory")]
    public void ExportServicesWithAnExplicitOutputAreAllowed(string type, string method, string parameter) => Assert.Null(Exclusion(Entry(method, parameter, type)));

    [Fact]
    public void LaterSettingsAndPrinterParametersCannotBeHiddenBehindAnAllowedFile()
    {
        ApiEntry e = Entry("ImportData");
        foreach (string parameter in new[] { "strScheme", "printer", "url", "database", "masterdata" })
        { Assert.NotNull(Exclusion(e with { Details = [e.Details[0] with { Parameters = [new("inputFile", "System.String"), new(parameter, "System.String")] }] })); }
        Assert.Null(Exclusion(Entry("ImportData", "strFullName", "PlcService")));
        ApiEntry insert = Entry("PageMacro", "strEMPFileName", "Insert");
        Assert.Null(Exclusion(insert));
        Assert.False(ApiTryGenerator.ReadMethod(Entry("GetData", "outputFile")));
    }

    [Theory]
    [InlineData("O:Eplan.EplApi.HEServices.DeviceService.ImportDeviceList", "strImportFilePath", "input", "fileFormat")]
    [InlineData("O:Eplan.EplApi.HEServices.DeviceService.ExportDeviceList", "strExportFilePath", "output", "fileFormat")]
    [InlineData("O:Eplan.EplApi.HEServices.DeviceService.ImportDeviceList", "strImportFilePath", "input", "strConverter")]
    [InlineData("O:Eplan.EplApi.HEServices.DeviceService.ExportDeviceList", "strExportFilePath", "output", "strConverter")]
    [InlineData("O:Eplan.EplApi.HEServices.HarnessService.ExportData", "strFileName", "output", "strLanguage")]
    [InlineData("O:Eplan.EplApi.HEServices.PlcService.ImportData", "strFullName", "input", "eImportMatch")]
    [InlineData("O:Eplan.EplApi.HEServices.PrePlanningService.ImportPropertyDefinitions", "strFileName", "input", "")]
    [InlineData("O:Eplan.EplApi.HEServices.PrePlanningService.ExportPropertyDefinitions", "strFileName", "output", "")]
    [InlineData("O:Eplan.EplApi.HEServices.PrePlanningService.ImportSegmentDefinitions", "strFileName", "input", "")]
    [InlineData("O:Eplan.EplApi.HEServices.PrePlanningService.ExportSegmentDefinitions", "strFileName", "output", "")]
    [InlineData("O:Eplan.EplApi.HEServices.PrePlanningService.ImportSegmentTemplates", "strFileName", "input", "")]
    [InlineData("O:Eplan.EplApi.HEServices.ProjectManagement.ReadProjectInfo", "strPrjInfoXml", "input", "")]
    public void PublishedFileShapesRemainTyped(string group, string parameter, string direction, string extra)
    {
        string owner = group.Contains("PrePlanning", StringComparison.Ordinal) ? "pProject" : "oProject";
        string[] names = extra.Length == 0 ? ["target", owner, parameter] : ["target", owner, parameter, extra];
        string[] shape = Assert.IsType<string[]>(ApiTryCatalog.Shape(group, names));
        Assert.Equal(direction, ApiTryCatalog.PathDirection(shape[1], parameter));
        Assert.Equal(["System.String"], ApiTryCatalog.ParameterShape(shape[1], parameter)!);
        Assert.Equal("write", ApiTryCatalog.MethodAccess(group));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlcConverterOverloadsRetainDistinctExactShapes(bool dialog)
    {
        const string Group = "O:Eplan.EplApi.HEServices.PlcService.ImportData";
        string[] names = ["target", "oProject", "strConverterID", "strLanguage", "strInputFileName", "eImportMatch", .. dialog ? new[] { "bShowCompareDlg" } : []];
        string[] shape = Assert.IsType<string[]>(ApiTryCatalog.Shape(Group, names));
        Assert.Equal("input", ApiTryCatalog.PathDirection(shape[1], "strInputFileName"));
        Assert.Equal("enum", ApiTryCatalog.ParameterShape(shape[1], "eImportMatch")![0]);
        if (dialog)
        { Assert.Equal(["System.Boolean"], ApiTryCatalog.ParameterShape(shape[1], "bShowCompareDlg")!); }
    }
}
