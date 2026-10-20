using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// export.pdf.plan, export.dxf.plan and export.image.plan against fake pages and schemes (docs/tools/eplan_export_*.md,
/// sections 2 to 5): what the export would be, checked without exporting.
/// </summary>
[Trait("Size", "Small")]
public sealed class ExportPlanOperationTests
{
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakePageReader _reader = new();
    private readonly FakeExporter _exporter = new();

    public ExportPlanOperationTests()
    {
        _catalog = new FakeCatalog(_line1, new OpenProject { Name = "Line2", Path = @"D:\Scratch\Line2" });
        _reader.Pages.AddRange([.. Enumerable.Range(1, 60).Select(i => new PageSummary { Name = "=GAA+A1&EFS1/" + i })]);
    }

    [Theory]
    [InlineData(ExportKind.Pdf, OperationNames.ExportPdfPlan)]
    [InlineData(ExportKind.Dxf, OperationNames.ExportDxfPlan)]
    [InlineData(ExportKind.Image, OperationNames.ExportImagePlan)]
    public void EachKind_HasItsOperationName(ExportKind kind, string name)
    {
        Assert.Equal(name, Operation(kind).Name);
    }

    [Fact]
    public void Plan_FindsThePagesAsPageGetDoes_AndNamesTheSchemeEplanUsedLast()
    {
        ExportPlanData plan = Plan(ExportKind.Pdf, new ExportArgs { Pages = ["=gaa+a1&efs1/1", "=GAA+A1&EFS1/3"] });

        Assert.Equal(("Line1", 2, "EPLAN_default_value"), (plan.Project, plan.PageCount, plan.Scheme));
        Assert.Empty(_exporter.Exports);
    }

    [Theory]
    [InlineData(ExportKind.Pdf)]
    [InlineData(ExportKind.Dxf)]
    public void Plan_WithoutPages_IsTheWholeProject(ExportKind kind)
    {
        Assert.Equal(60, Plan(kind, new ExportArgs()).PageCount);
    }

    [Fact]
    public void Plan_OfAnotherProject_AndAGivenScheme()
    {
        _exporter.SchemeNames[ExportKind.Dxf].Add("DXF für Mechanik");

        ExportPlanData plan = Plan(ExportKind.Dxf, new ExportArgs { Project = "Line2", Pages = ["=GAA+A1&EFS1/1"], Scheme = " DXF für Mechanik " });

        Assert.Equal(("Line2", "DXF für Mechanik"), (plan.Project, plan.Scheme));
    }

    [Fact]
    public void Plan_AnUnknownScheme_IsInvalid_WithTheSchemesThereAreAsCandidates()
    {
        _exporter.SchemeNames[ExportKind.Pdf].AddRange(Enumerable.Range(1, 25).Select(i => "S" + i));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(ExportKind.Pdf, new ExportArgs { Scheme = "pdf_a3" })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("omit it for the scheme EPLAN used last", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Equal(20, error.Metadata["candidates"].Split('\n').Length);
        Assert.Equal(("EPLAN_default_value", "27"), (error.Metadata["candidates"].Split('\n')[0], error.Metadata["candidateCount"]));
    }

    [Fact]
    public void Plan_ImagesHaveNoScheme()
    {
        ExportPlanData plan = Plan(ExportKind.Image, new ExportArgs { Pages = ["=GAA+A1&EFS1/1"], Scheme = "ignored" });

        Assert.Equal((1, (string?)null), (plan.PageCount, plan.Scheme));
    }

    [Fact]
    public void Plan_ListsEveryMissingPage()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(ExportKind.Pdf, new ExportArgs { Pages = ["=GAA+A1&EFS1/99", "=GAA+A1&EFS1/1", "=X/1"] })).Error;

        Assert.Equal((ErrorReason.PageNotFound, "=GAA+A1&EFS1/99\n=X/1"), (error.Reason, error.Metadata!["missing"]));
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new ExportArgs { Pages = ["=GAA+A1&EFS1/1", "=gaa+a1&efs1/1", string.Empty], Format = "gif", WidthPx = 100 };

        string[] violations = Assert.Throws<BridgeErrorException>(() => Plan(ExportKind.Image, args)).Error.Metadata!["violations"].Split('\n');

        Assert.Equal(4, violations.Length);
        Assert.Contains("pages[1] repeats pages[0]", violations[0], StringComparison.Ordinal);
        Assert.Contains("pages[2] is required", violations[1], StringComparison.Ordinal);
        Assert.Contains("'format' must be png or jpg", violations[2], StringComparison.Ordinal);
        Assert.Contains("'width_px' must be between 200 and 4000", violations[3], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExportKind.Pdf, 0)]
    [InlineData(ExportKind.Pdf, 51)]
    [InlineData(ExportKind.Dxf, 0)]
    [InlineData(ExportKind.Image, 0)]
    [InlineData(ExportKind.Image, 21)]
    public void Plan_NoPagesOrTooMany_IsInvalid(ExportKind kind, int count)
    {
        var args = new ExportArgs { Pages = [.. Enumerable.Range(1, count).Select(i => "=GAA+A1&EFS1/" + i)] };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(kind, args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains(kind == ExportKind.Image ? "1 to 20" : "1 to 50", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ImagesNeedPages()
    {
        Assert.Contains("'pages' must hold 1 to 20", Assert.Throws<BridgeErrorException>(() => Plan(ExportKind.Image, new ExportArgs())).Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AnUnknownColour_IsInvalidForPdfOnly()
    {
        Assert.Contains("'color' must be", Assert.Throws<BridgeErrorException>(() => Plan(ExportKind.Pdf, new ExportArgs { Color = "red" })).Error.Message, StringComparison.Ordinal);
        Assert.Equal(60, Plan(ExportKind.Dxf, new ExportArgs { Color = "red" }).PageCount);
    }

    [Fact]
    public void Plan_OfAProjectWithoutPages_IsInvalid()
    {
        _reader.Pages.Clear();

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(ExportKind.Pdf, new ExportArgs())).Error.Reason);
    }

    [Fact]
    public void Plan_OfAProjectNotOpen_IsProjectNotOpen()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Assert.Throws<BridgeErrorException>(() => Plan(ExportKind.Pdf, new ExportArgs { Project = "Line9" })).Error.Reason);
    }

    private ExportPlanOperation Operation(ExportKind kind) => new(kind, _catalog, _reader, _exporter);

    private ExportPlanData Plan(ExportKind kind, ExportArgs args) => (ExportPlanData)Operation(kind).Execute(args)!;
}
