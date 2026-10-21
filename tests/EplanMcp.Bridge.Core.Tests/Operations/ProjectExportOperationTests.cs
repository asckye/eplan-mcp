using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The plans and runs of the exports of a whole project against a fake exporter that writes into a real temporary
/// folder (docs/tools/eplan_export_pxf.md, section 5; SEC-20), through the router where cancels matter.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectExportOperationTests : IDisposable
{
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakeProjectExporter _exporter = new();
    private readonly ListLog _log = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-project-export-").FullName + Path.DirectorySeparatorChar;

    public ProjectExportOperationTests()
    {
        _catalog = new FakeCatalog(_line1, new OpenProject { Name = "Line2", Path = @"D:\Scratch\Line2" });
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Plan_NamesTheProject_AndExportsNothing()
    {
        var plan = (ExportPlanData)new ProjectExportPlanOperation(ProjectExportKind.Pxf, _catalog).Execute(new ExportArgs { Project = "Line2" })!;

        Assert.Equal(("Line2", 0, (string?)null), (plan.Project, plan.PageCount, plan.Scheme));
        Assert.Equal(OperationNames.ExportPxfPlan, new ProjectExportPlanOperation(ProjectExportKind.Pxf, _catalog).Name);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void Plan_AProjectThatIsNotOpen_IsProjectNotOpen()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new ProjectExportPlanOperation(ProjectExportKind.Pxf, _catalog).Execute(new ExportArgs { Project = "Line9" })).Error;

        Assert.Equal(ErrorReason.ProjectNotOpen, error.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pxf_IsOneEpjFileInTheHostsFolder_WithConnectionsAndMasterDataAsAsked(bool masterData)
    {
        ExportRunData data = Run(new ExportArgs { Directory = _folder, FileName = "电气 Line1", IncludeMasterData = masterData });

        Assert.Equal(["电气 Line1.epj"], Files());
        Assert.Equal([$@"pxf D:\Scratch\Line1 电气 Line1 {masterData}"], _exporter.Exports);
        Assert.Equal(("Line1", 0, (string?)null, (string?)null), (data.Project, data.PageCount, data.Scheme, data.Warning));
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("C:x")]
    [InlineData("a/b")]
    [InlineData("Line1.epj")]
    [InlineData("Line1.EPJ")]
    [InlineData("")]
    [InlineData(null)]
    public void Pxf_AFileNameThatIsNoPlainNameWithoutTheExtension_IsRefusedBeforeExporting(string? fileName)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = _folder, FileName = fileName })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void Parts_IsOneXmlFileOfTheGivenNameInTheHostsFolder()
    {
        ExportRunData data = Run(new ExportArgs { Directory = _folder, FileName = "部件 Line1.xml" }, ProjectExportKind.PartsList);

        Assert.Equal(["部件 Line1.xml"], Files());
        Assert.Equal([@"parts D:\Scratch\Line1 部件 Line1.xml"], _exporter.Exports);
        Assert.Equal("Line1", data.Project);
        Assert.Equal(
            (OperationNames.ExportPartsPlan, OperationNames.ExportPartsRun),
            (new ProjectExportPlanOperation(ProjectExportKind.PartsList, _catalog).Name, new ProjectExportRunOperation(ProjectExportKind.PartsList, _catalog, _exporter).Name));
    }

    [Theory]
    [InlineData("parts")]
    [InlineData("parts.csv")]
    [InlineData(".xml")]
    [InlineData(@"..\parts.xml")]
    public void Parts_AFileNameThatIsNoPlainXmlName_IsRefusedBeforeExporting(string fileName)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = _folder, FileName = fileName }, ProjectExportKind.PartsList)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("ending in .xml", error.Message, StringComparison.Ordinal);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void AFolderThatIsMissingNotEmptyOrNotFullyQualified_IsRefused()
    {
        File.WriteAllText(Path.Combine(_folder, "old.epj"), "x");

        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = _folder, FileName = "a" })).Error.Reason);
        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = Path.Combine(_folder, "missing"), FileName = "a" })).Error.Reason);
        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = "relative", FileName = "a" })).Error.Reason);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void AFailedExport_IsTheExportersError()
    {
        _exporter.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not export the project as PXF.");

        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = _folder, FileName = "a" })).Error.Reason);
    }

    [Fact]
    public void Pxf_CancelledWhileEplanExports_IsReportedWhenTheCallReturns()
    {
        var router = new OperationRouter([new ProjectExportRunOperation(ProjectExportKind.Pxf, _catalog, _exporter)], new InlineDispatcher(), null, _log, TimeSpan.FromSeconds(10));
        _exporter.During = () => Assert.True((bool)router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "c1" }, "cancel")["found"]!);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.ExportPxfRun, BridgeJson.FromObject(new ExportArgs { Directory = _folder, FileName = "a" }), "c1")).Error;

        Assert.Equal(ErrorReason.Cancelled, error.Reason);
        Assert.Equal(["a.epj"], Files()); // the call ran to its end; the Host deletes the folder
    }

    private ExportRunData Run(ExportArgs args, ProjectExportKind kind = ProjectExportKind.Pxf) => (ExportRunData)new ProjectExportRunOperation(kind, _catalog, _exporter).Execute(args)!;

    private string[] Files() => [.. ExportFolderCheck.Files(_folder).Order(StringComparer.Ordinal)];
}
