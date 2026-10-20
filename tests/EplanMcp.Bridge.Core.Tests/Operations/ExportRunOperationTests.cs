using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// export.pdf.run, export.dxf.run and export.image.run against a fake exporter that writes into a real temporary folder
/// (docs/tools/eplan_export_*.md, section 5; SEC-20), through the router where progress and cancels matter.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ExportRunOperationTests : IDisposable
{
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _reader = new();
    private readonly FakeExporter _exporter = new();
    private readonly ListLog _log = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-export-").FullName + Path.DirectorySeparatorChar;

    public ExportRunOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _reader.Pages.AddRange([.. Enumerable.Range(1, 5).Select(i => new PageSummary { Name = "=GAA+A1&EFS1/" + i })]);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Pdf_IsOneFileOfTheGivenNameInTheHostsFolder_WithTheSchemeEplanUsedLast()
    {
        ExportRunData data = Run(ExportKind.Pdf, new ExportArgs { Pages = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"], Color = "grey", Directory = _folder, FileName = "电气图.pdf" });

        Assert.Equal(["电气图.pdf"], Files());
        Assert.Equal(["pdf =GAA+A1&EFS1/1,=GAA+A1&EFS1/2 '' Grey 电气图.pdf"], _exporter.Exports);
        Assert.Equal(("Line1", 2, "EPLAN_default_value", (string?)null, (List<ExportedPage>?)null), (data.Project, data.PageCount, data.Scheme, data.Warning, data.Files));
    }

    [Fact]
    public void Pdf_WithANamedScheme_SetsEplansLastUsedSchemeBack()
    {
        _exporter.RememberScheme = true;

        ExportRunData data = Run(ExportKind.Pdf, new ExportArgs { Scheme = "PDF_A3", Directory = _folder, FileName = "a.pdf" });

        Assert.Equal(("PDF_A3", 5), (data.Scheme, data.PageCount));
        Assert.Equal("EPLAN_default_value", _exporter.LastUsed[ExportKind.Pdf]);
        Assert.Null(data.Warning);
    }

    [Fact]
    public void Pdf_WhenTheLastUsedSchemeCannotBeSetBack_SaysSoInAWarning()
    {
        (_exporter.RememberScheme, _exporter.RefuseSetLastUsed) = (true, true);

        ExportRunData data = Run(ExportKind.Pdf, new ExportArgs { Scheme = "PDF_A3", Directory = _folder, FileName = "a.pdf" });

        Assert.Contains("could not be set back to 'EPLAN_default_value'", data.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedExport_StillSetsTheLastUsedSchemeBack()
    {
        (_exporter.RememberScheme, _exporter.Failure) = (true, new InvalidOperationException("EPLAN failed"));

        Assert.Throws<InvalidOperationException>(() => Run(ExportKind.Dxf, new ExportArgs { Scheme = "DWG_2018", Directory = _folder }));

        Assert.Equal("Standard Settings", _exporter.LastUsed[ExportKind.Dxf]);
    }

    [Theory]
    [InlineData(@"..\x.pdf")]
    [InlineData("C:x.pdf")]
    [InlineData("x")]
    [InlineData(null)]
    public void Pdf_AFileNameThatIsNoPlainPdfName_IsRefusedBeforeExporting(string? fileName)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(ExportKind.Pdf, new ExportArgs { Directory = _folder, FileName = fileName })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void Dxf_IsOneFilePerPageInTheSchemesSubfolders()
    {
        ExportRunData data = Run(ExportKind.Dxf, new ExportArgs { Pages = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"], Directory = _folder });

        Assert.Equal([@"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf", @"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_2.dxf"], Files());
        Assert.Equal(("Standard Settings", 2), (data.Scheme, data.PageCount));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"C:folder")]
    [InlineData(null)]
    public void AFolderThatIsNotFullyQualified_IsRefused(string? directory)
    {
        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(ExportKind.Dxf, new ExportArgs { Directory = directory })).Error.Reason);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void AFolderThatIsMissingOrNotEmpty_IsRefused()
    {
        File.WriteAllText(Path.Combine(_folder, "old.pdf"), "x");

        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(ExportKind.Dxf, new ExportArgs { Directory = _folder })).Error.Reason);
        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => Run(ExportKind.Dxf, new ExportArgs { Directory = Path.Combine(_folder, "missing") })).Error.Reason);
        Assert.Empty(_exporter.Exports);
    }

    [Fact]
    public void Images_AreOnePerPage_EachFoundAsTheNewFile_WithProgressBeforeEachPage()
    {
        _exporter.ImageName = page => "img" + page[^1];
        var slot = new ProgressSlot();
        var seen = new List<string?>();
        _exporter.BeforeImage = _ => seen.Add(slot.Take()?.Message);

        JToken data = Router().Invoke(OperationNames.ExportImageRun, Args(new ExportArgs { Pages = ["=GAA+A1&EFS1/3", "=GAA+A1&EFS1/1"], Format = "jpg", WidthPx = 800, Directory = _folder }), "c1", slot);

        Assert.Equal(["Exporting page 1 of 2", "Exporting page 2 of 2"], seen);
        Assert.Equal("Exported 2 of 2 pages", slot.Take()?.Message);
        Assert.Equal(["image =GAA+A1&EFS1/3 Jpg 800", "image =GAA+A1&EFS1/1 Jpg 800"], _exporter.Exports);
        Assert.Equal(
            [("=GAA+A1&EFS1/3", "img3.jpg"), ("=GAA+A1&EFS1/1", "img1.jpg")],
            data["files"]!.Select(f => ((string)f["page"]!, (string)f["path"]!)));
    }

    [Fact]
    public void Images_CancelledDuringAPage_StopBeforeTheNext()
    {
        OperationRouter router = Router();
        _exporter.BeforeImage = done => _ = done == 1 && Cancel(router, "c1");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.ExportImageRun, Args(new ExportArgs { Pages = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAA+A1&EFS1/3"], Directory = _folder }), "c1")).Error;

        Assert.Equal((ErrorReason.Cancelled, (string?)null), (error.Reason, error.Execution));
        Assert.Equal(2, _exporter.Exports.Count);
    }

    [Fact]
    public void Images_APageThatAddsNoNewFile_FailsTheExport()
    {
        _exporter.ImageName = _ => "same";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(ExportKind.Image, new ExportArgs { Pages = ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"], Directory = _folder })).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, "=GAA+A1&EFS1/2", "0"), (error.Reason, error.Metadata!["page"], error.Metadata["newFiles"]));
    }

    [Fact]
    public void Pdf_CancelledWhileEplanExports_IsReportedWhenTheCallReturns()
    {
        OperationRouter router = null!;
        _exporter.RememberScheme = true;
        var cancelling = new CancellingExporter(_exporter, () => Cancel(router, "c1"));
        router = new OperationRouter([new ExportRunOperation(ExportKind.Pdf, _catalog, _reader, cancelling)], new InlineDispatcher(), null, _log, TimeSpan.FromSeconds(10));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => router.Invoke(OperationNames.ExportPdfRun, Args(new ExportArgs { Scheme = "PDF_A3", Directory = _folder, FileName = "a.pdf" }), "c1")).Error;

        Assert.Equal(ErrorReason.Cancelled, error.Reason);
        Assert.Equal(["a.pdf"], Files()); // the call ran to its end; the Host deletes the folder
        Assert.Equal("EPLAN_default_value", _exporter.LastUsed[ExportKind.Pdf]);
    }

    private ExportRunData Run(ExportKind kind, ExportArgs args) => (ExportRunData)new ExportRunOperation(kind, _catalog, _reader, _exporter).Execute(args)!;

    private OperationRouter Router() => new(
        [new ExportRunOperation(ExportKind.Pdf, _catalog, _reader, _exporter), new ExportRunOperation(ExportKind.Image, _catalog, _reader, _exporter)],
        new InlineDispatcher(),
        null,
        _log,
        TimeSpan.FromSeconds(10));

    private static JToken Args(ExportArgs args) => BridgeJson.FromObject(args);

    private static bool Cancel(OperationRouter router, string requestId) =>
        (bool)router.Invoke(OperationNames.OperationCancel, new JObject { ["requestId"] = requestId }, "cancel")["found"]!;

    private string[] Files() => [.. ExportFolderCheck.Files(_folder).Order(StringComparer.Ordinal)];

    /// <summary>Stands for a cancel that arrives while EPLAN is inside the export call.</summary>
    private sealed class CancellingExporter(FakeExporter inner, Func<bool> cancel) : IExporter
    {
        public bool SchemeExists(ExportKind kind, string scheme) => inner.SchemeExists(kind, scheme);

        public IReadOnlyList<string> Schemes(ExportKind kind) => inner.Schemes(kind);

        public string LastUsedScheme(ExportKind kind) => inner.LastUsedScheme(kind);

        public bool TrySetLastUsedScheme(ExportKind kind, string scheme) => inner.TrySetLastUsedScheme(kind, scheme);

        public void ExportPdf(string projectPath, IReadOnlyList<string> pages, string scheme, ExportColor color, string file)
        {
            Assert.True(cancel());
            inner.ExportPdf(projectPath, pages, scheme, color, file);
        }

        public void ExportDxf(string projectPath, IReadOnlyList<string> pages, string scheme, string directory) => inner.ExportDxf(projectPath, pages, scheme, directory);

        public void ExportImage(string projectPath, string page, string directory, ImageFormat format, int widthPx) => inner.ExportImage(projectPath, page, directory, format, widthPx);
    }
}
