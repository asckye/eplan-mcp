using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// import.dxf.plan against fake pages and a drawing in a temporary folder, standing for the file the Host resolved below
/// its import root (docs/tools/eplan_import_dxf.md, sections 2, 4, 5 and 7; SEC-31): the change names the file, its
/// size and SHA-256; the file is read again, so one that changed since the Host read it is PLAN_STALE.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ImportDxfOperationTests : IDisposable
{
    private const string Page = "=GAA+A1&EFS1/92";

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-import-dxf-").FullName;
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _pages = new();
    private readonly Importer _importer = new();
    private readonly string _file;

    public ImportDxfOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _importer.Pages[Page] = new MacroTargetPage { Name = Page, Type = "Circuit", Objects = ["1/1/1/0"] };
        _pages.Pages.Add(new PageSummary { Name = Page });
        _file = Path.Combine(_root, "drawings", "4685200F.DXF");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllBytes(_file, new byte[25 * 1024]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Plan_IsOneDrawingChange_NamingTheFileItsSizeAndSha256()
    {
        WritePlan plan = Plan(Args());

        Assert.Equal(("Line1", @"D:\Scratch\Line1"), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("create", "drawing", Page), (change.Action, change.Kind, change.Name));
        Assert.Equal($"4685200F.DXF (25 KB, sha256 {Sha256()[..12]}…), scale fit, center", change.NewValue);
        Assert.Equal(("drawings/4685200F.DXF", Sha256(), "25600", "fit", "center"), (plan.Details!["file"], plan.Details["sha256"], plan.Details["bytes"], plan.Details["scale"], plan.Details["position"]));
        Assert.DoesNotContain(_root, change.NewValue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_TakesScaleNoneAndTheLowerLeftCorner()
    {
        ImportDxfArgs args = Args();
        args.Scale = "none";
        args.Position = "lower_left";

        Assert.EndsWith(", scale none, lower_left", Plan(args).Changes[0].NewValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new ImportDxfArgs { Page = "", File = " ", FilePath = @"drawings\plan.dxf", Sha256 = "ABC", Scale = "half", Position = "top" };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal(
            [
                "'project' is required for writes: a name or path from eplan_project_list.",
                "'page' is required: a full page name such as =GAA+A1&EFS1/92; eplan_page_create makes a new one.",
                "'file' is required: a path below the import folder.",
                "'filePath' must be the full path the Host resolved below its import folder, ending with .dxf or .dwg.",
                "'sha256' must be the file's SHA-256 as 64 lowercase hex digits.",
                "'scale' must be fit or none.",
                "'position' must be center or lower_left.",
            ],
            error.Error.Metadata!["violations"].Split('\n'));
    }

    [Fact]
    public void Plan_AFileOfAnotherKind_IsInvalid()
    {
        ImportDxfArgs args = Args();
        args.FilePath = Path.ChangeExtension(_file, ".pdf");

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));

        Assert.Contains("ending with .dxf or .dwg", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AMissingPage_IsPageNotFound()
    {
        ImportDxfArgs args = Args();
        args.Page = "=GAA+A1&EFS1/999";

        Assert.Equal(ErrorReason.PageNotFound, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Plan_AFileGoneSinceTheHostReadIt_IsFileNotFound_WithoutItsLocalPath()
    {
        ImportDxfArgs args = Args();
        File.Delete(_file);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));

        Assert.Equal((ErrorReason.FileNotFound, "drawings/4685200F.DXF"), (error.Error.Reason, error.Error.Metadata!["file"]));
        Assert.DoesNotContain(_root, error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_AFileChangedSinceTheHostReadIt_IsPlanStale()
    {
        ImportDxfArgs args = Args();
        File.WriteAllBytes(_file, new byte[25 * 1024 + 1]);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
    }

    [Fact]
    public void Fingerprint_ChangesWithThePagesObjects_AndStaysOtherwise()
    {
        string first = Plan(Args()).Fingerprint;
        string again = Plan(Args()).Fingerprint;
        _importer.Pages[Page].Objects.Add("1/1/2/0");

        Assert.Equal(first, again);
        Assert.NotEqual(first, Plan(Args()).Fingerprint);
    }

    [Theory]
    [InlineData(900, "900 bytes")]
    [InlineData(25 * 1024, "25 KB")]
    [InlineData(598L * 1024 * 1024, "598 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2 GB")]
    public void Size_IsWordedAsTheHostsSummaries(long bytes, string text)
    {
        Assert.Equal(text, ImportSource.Size(bytes));
    }

    private ImportDxfArgs Args() => new() { Project = "Line1", Page = Page, File = "drawings/4685200F.DXF", FilePath = _file, Sha256 = Sha256() };

    private string Sha256() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_file)));

    private WritePlan Plan(ImportDxfArgs args) => (WritePlan)new ImportDxfPlanOperation(_catalog, _pages, _importer).Execute(args)!;

    /// <summary>The pages a drawing could go on.</summary>
    private sealed class Importer : IDrawingImporter
    {
        public Dictionary<string, MacroTargetPage> Pages { get; } = new(StringComparer.Ordinal);

        public MacroTargetPage? ReadPage(string projectPath, string pageName) => Pages.GetValueOrDefault(pageName);
    }
}
