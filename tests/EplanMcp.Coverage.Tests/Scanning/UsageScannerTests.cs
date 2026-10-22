using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Tests.Scanning;

[Trait("Size", "Medium")]
public sealed class UsageScannerTests
{
    private const string Op = "EplanMcp.Bridge.Core.Operations.ExportRunOperation";
    private const string Kind = "EplanMcp.Bridge.Core.Operations.ExportKind";
    private const string ExportType = "Eplan.EplApi.HEServices.Export";

    private static readonly SortedDictionary<string, List<UsageSite>> _usages = new UsageScanner(FakeBridge.Compile(), FakeBridge.Root).Scan(
    [
        new OperationRoot("export.pdf.run", Op, Kind, "Pdf"),
        new OperationRoot("export.dxf.run", Op, Kind, "Dxf"),
        new OperationRoot("export.image.run", Op, Kind, "Image"),
    ]);

    [Fact]
    public void Scan_TheExactOverload_IsItsDocumentationId()
    {
        UsageSite site = Assert.Single(_usages[$"M:{ExportType}.PdfPages(System.String,System.Int32)"]);

        Assert.Equal(("FakeExporter", "ExportPdf", "src/EplanMcp.Bridge.Eplan/Adapters/FakeExporter.cs"), (site.Adapter, site.Member, site.File));
        Assert.Equal(["call"], site.Access);
    }

    [Fact]
    public void Scan_PropertiesIndexersConstructorsAndEnums_AreRecordedWithTheirAccess()
    {
        Assert.Equal(["get", "set"], Access($"P:{ExportType}.Count"));
        Assert.Equal(["get", "set"], Access($"P:{ExportType}.Item(System.Int32)"));
        Assert.Equal(["new"], Access($"M:{ExportType}.#ctor", "Shared"));
        Assert.Equal(["read"], Access($"F:{ExportType}.Format.JPG"));
        Assert.Equal(["type"], Access($"T:{ExportType}", "Shared"));
    }

    [Fact]
    public void Scan_AKindSpecializedOperation_ReachesOnlyTheBranchesOfItsKind()
    {
        Assert.Equal(["export.pdf.run"], Operations($"M:{ExportType}.PdfPages(System.String,System.Int32)"));
        Assert.Equal(["export.dxf.run"], Operations($"M:{ExportType}.Dxf(System.String)"));
        Assert.Equal(["export.image.run"], Operations($"M:{ExportType}.Image({ExportType}.Format)"));
        Assert.Equal(["export.dxf.run"], Operations($"M:{ExportType}.PdfPages(System.String)"));
        Assert.Equal(["export.dxf.run", "export.image.run", "export.pdf.run"], Operations($"P:{ExportType}.Count"));
    }

    [Fact]
    public void Scan_ActionNamesAndTypeParameters_BecomeActionIds()
    {
        string[] actions = [.. _usages.Keys.Where(k => k.StartsWith("A:", StringComparison.Ordinal))];

        Assert.Equal(["A:XPrjActionProjectClose", "A:export", "A:export/PDFPROJECTSCHEME", "A:label", "A:label/EXPORT"], actions);
        UsageSite branch = Assert.Single(_usages["A:export/PDFPROJECTSCHEME"]);
        Assert.Equal(("FakeAddIn", "ExportScheme", "action"), (branch.Adapter, branch.Member, Assert.Single(branch.Access)));
    }

    [Fact]
    public void Scan_TheAddInsInterface_IsABaseTypeAndAnImplementedMember_ReachedByNoOperation()
    {
        UsageSite implemented = Assert.Single(_usages["M:Eplan.EplApi.ApplicationFramework.IEplAddIn.OnInit"]);
        UsageSite declared = Assert.Single(_usages["T:Eplan.EplApi.ApplicationFramework.IEplAddIn"]);

        Assert.Equal(("OnInit", "implements"), (implemented.Member, Assert.Single(implemented.Access)));
        Assert.Equal(("(base)", "type"), (declared.Member, Assert.Single(declared.Access)));
        Assert.Empty(implemented.Operations);
    }

    [Fact]
    public void Scan_CodeOutsideTheAdapterFolder_IsNotAUseSite()
    {
        Assert.DoesNotContain(_usages.Values.SelectMany(s => s), s => !s.File.StartsWith(UsageScanner.AdapterFolder, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Access(string id, string? member = null) => Assert.Single(_usages[id], s => member is null || s.Member == member).Access;

    private static IReadOnlyList<string> Operations(string id) => Assert.Single(_usages[id]).Operations;
}
