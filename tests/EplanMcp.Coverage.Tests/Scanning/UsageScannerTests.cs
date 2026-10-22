using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Tests.Scanning;

[Trait("Size", "Medium")]
public sealed class UsageScannerTests
{
    private const string ExportType = "Eplan.EplApi.HEServices.Export";

    private static readonly SortedDictionary<string, List<UsageSite>> _usages = new UsageScanner(FakeBridge.Compile(), FakeBridge.Root).Scan();

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
    public void Scan_CodeOutsideTheAdapterFolder_IsNotAUseSite()
    {
        Assert.DoesNotContain(_usages.Values.SelectMany(s => s), s => !s.File.StartsWith(UsageScanner.AdapterFolder, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Access(string id, string? member = null) => Assert.Single(_usages[id], s => member is null || s.Member == member).Access;
}
