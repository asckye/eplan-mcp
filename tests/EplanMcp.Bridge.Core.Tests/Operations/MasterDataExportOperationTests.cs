using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The plans and runs of the database exports against a fake exporter that writes into a real temporary folder
/// (docs/tools/eplan_part_export.md, sections 2 to 5; SEC-20), through the router where cancels matter.
/// </summary>
[Trait("Size", "Medium")]
public sealed class MasterDataExportOperationTests : IDisposable
{
    private readonly FakeMasterDataExporter _exporter = new();
    private readonly ListLog _log = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-database-export-").FullName + Path.DirectorySeparatorChar;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(null, ".xml")]
    [InlineData(" EDZ ", ".edz")]
    public void PartsPlan_NamesTheDatabaseAndTheExtension_AndExportsNothing(string? format, string extension)
    {
        var data = (ExportPlanData)new MasterDataExportPlanOperation(MasterDataExportKind.Parts, _exporter).Execute(new ExportArgs { Format = format })!;

        Assert.Equal((string.Empty, @"$(MD_PARTS)\EPLAN_parts.alk", extension), (data.Project, data.Database, data.Extension));
        Assert.Equal(
            (OperationNames.PartExportPlan, OperationNames.PartExportRun),
            (new MasterDataExportPlanOperation(MasterDataExportKind.Parts, _exporter).Name, new MasterDataExportRunOperation(MasterDataExportKind.Parts, _exporter).Name));
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void Parts_All_IsOneXmlFileInTheHostsFolder_WithEveryPartCounted()
    {
        ExportRunData data = Run(new ExportArgs { Directory = _folder, FileName = "部件库.xml" });

        Assert.Equal(["部件库.xml"], Files());
        Assert.Equal(["parts Xml all 部件库.xml"], _exporter.Exports);
        Assert.Equal((@"$(MD_PARTS)\EPLAN_parts.alk", (int?)4, string.Empty), (data.Database, data.PartCount, data.Project));
    }

    [Fact]
    public void Parts_Given_AreExportedOnceEach_WithEveryVariant_AsEdz()
    {
        ExportRunData data = Run(new ExportArgs { Format = "edz", Parts = [" PXC.3209510", "SIE.3RH2122-1HB40", "PXC.3209510 "], Directory = _folder, FileName = "relays.edz" });

        Assert.Equal(["parts Edz PXC.3209510,SIE.3RH2122-1HB40 relays.edz"], _exporter.Exports);
        Assert.Equal(3, data.PartCount);
    }

    [Fact]
    public void Parts_AMissingPartNumber_IsPartNotFound_InThePlan_AndTheRunExportsNothing()
    {
        string[] missing = [.. Enumerable.Range(0, 25).Select(i => "NONE." + i)];
        var args = new ExportArgs { Parts = ["SIE.3RH2122-1HB40", .. missing], Directory = _folder, FileName = "parts.xml" };

        BridgeError plan = Assert.Throws<BridgeErrorException>(() => new MasterDataExportPlanOperation(MasterDataExportKind.Parts, _exporter).Execute(args)).Error;
        BridgeError run = Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

        Assert.Equal((ErrorReason.PartNotFound, false, "25"), (plan.Reason, plan.Retryable, plan.Metadata!["missingCount"]));
        Assert.Equal(missing.Take(20), plan.Metadata["missing"].Split('\n'));
        Assert.Contains("eplan_part_search", plan.Message, StringComparison.Ordinal);
        Assert.Equal(ErrorReason.PartNotFound, run.Reason);
        Assert.Empty(Files());
        Assert.Empty(_exporter.Exports);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Parts_NoneOrMoreThan500_AreInvalid(int count)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Parts = [.. Enumerable.Range(0, count).Select(i => "P." + i)], Directory = _folder, FileName = "a.xml" })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'parts' must hold 1 to 500", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Parts_ABlankPartNumberAndAnUnknownFormat_AreReportedTogether()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new MasterDataExportPlanOperation(MasterDataExportKind.Parts, _exporter).Execute(new ExportArgs { Format = "csv", Parts = ["SIE.3RH2122-1HB40", " "] })).Error;

        string violations = error.Metadata!["violations"];
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'format' must be xml or edz.", violations, StringComparison.Ordinal);
        Assert.Contains("parts[1] must be a part number", violations, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("edz", "parts.xml", "ending in .edz")]
    [InlineData(null, @"..\parts.xml", "ending in .xml")]
    [InlineData(null, ".xml", "ending in .xml")]
    public void Parts_AFileNameThatIsNoPlainNameWithTheFormatsExtension_IsRefusedBeforeExporting(string? format, string fileName, string message)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Format = format, Directory = _folder, FileName = fileName })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void ASqlServerLogin_NeverLeavesTheBridge()
    {
        _exporter.PartsDatabase = "SQL01|EplanParts|2|user|secret";

        var plan = (ExportPlanData)new MasterDataExportPlanOperation(MasterDataExportKind.Parts, _exporter).Execute(new ExportArgs())!;
        ExportRunData run = Run(new ExportArgs { Directory = _folder, FileName = "p.xml" });

        Assert.Equal(("SQL01|EplanParts", "SQL01|EplanParts"), (plan.Database, run.Database));
    }

    [Fact]
    public void AFolderThatIsMissingOrNotEmpty_IsRefused()
    {
        File.WriteAllText(Path.Combine(_folder, "old.xml"), "x");

        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = _folder, FileName = "a.xml" })).Error.Reason);
        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = Path.Combine(_folder, "missing"), FileName = "a.xml" })).Error.Reason);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void AFailedExport_IsTheExportersError()
    {
        _exporter.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not export its parts database, so no files were kept.");

        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => Run(new ExportArgs { Directory = _folder, FileName = "a.xml" })).Error.Reason);
    }

    [Fact]
    public void Parts_CancelledWhileEplanExports_IsReportedWhenTheCallReturns()
    {
        var router = new OperationRouter([new MasterDataExportRunOperation(MasterDataExportKind.Parts, _exporter)], new InlineDispatcher(), null, _log, TimeSpan.FromSeconds(10));
        _exporter.During = () => Assert.True((bool)router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = "c1" }, "cancel")["found"]!);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.PartExportRun, BridgeJson.FromObject(new ExportArgs { Directory = _folder, FileName = "a.xml" }), "c1")).Error;

        Assert.Equal(ErrorReason.Cancelled, error.Reason);
        Assert.Equal(["a.xml"], Files()); // the call ran to its end; the Host deletes the folder
    }

    private ExportRunData Run(ExportArgs args, MasterDataExportKind kind = MasterDataExportKind.Parts) => (ExportRunData)new MasterDataExportRunOperation(kind, _exporter).Execute(args)!;

    private string[] Files() => [.. ExportFolderCheck.Files(_folder).Order(StringComparer.Ordinal)];
}
