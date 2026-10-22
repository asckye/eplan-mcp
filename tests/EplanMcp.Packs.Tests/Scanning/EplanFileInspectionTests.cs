using System.Text;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs.Tests.Scanning;

/// <summary>
/// What the scanner reads from EPLAN's files, on synthetic files shaped like the real ones (api-notes A35). No EPLAN
/// file is committed (CLAUDE.md rule 4); <see cref="RealEplanFilesTests"/> reads the installed sample read-only.
/// </summary>
[Trait("Size", "Medium")]
public sealed class EplanFileInspectionTests : IDisposable
{
    private readonly TempFolder _folder = new();

    [Fact]
    public async Task BasicProject_7zSignature_IsAFactAndItsContentsAGap()
    {
        _folder.Write("In/GB_bas001.zw9", [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04, 0x50, 0x1E]);

        ScanResult result = await ScanAsync("In");

        Assert.Equal([("archive_format", "7z"), ("archive_format_version", "0.4")], Facts(result));
        Assert.Equal((FindingSeverity.Gap, "archive_not_inspected"), Assert.Single(result.Findings) is var f ? (f.Severity, f.Code) : default);
    }

    [Fact]
    public async Task ProjectBackup_NotA7zArchive_IsAnError()
    {
        _folder.Write("In/Plant.zw1", "PK\u0003\u0004 a zip, not EPLAN's 7z");

        ScanResult result = await ScanAsync("In");

        Assert.Equal((FindingSeverity.Error, "not_7z"), Assert.Single(result.Findings) is var f ? (f.Severity, f.Code) : default);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task Project_VersionsComeFromTheLinkFileAndProjectInfoById_AndNothingPersonal()
    {
        WriteProject("In/Plant", """
            <?xml version="1.0" encoding="utf-8" ?>
            <Properties>
             <Property name="创建者" id="10020" index="0" type="7">Wang Wei</Property>
             <Property name="公司地址 1" id="10016" index="0" type="7">Somewhere Road 1</Property>
             <Property name="最新更改或更新的 Eplan 版本" id="10043" index="0" type="7">2026.0.3</Property>
             <Property name="Internal: Highest used Eplan version" id="10088" index="0" type="7">2026.0.3.25421</Property>
            </Properties>
            """);

        ScanResult result = await ScanAsync("In");

        Assert.Equal([("database_version", "18.0"), ("eplan_highest_version", "2026.0.3.25421"), ("eplan_version", "2026.0.3")], Facts(result).Order());
        Assert.Equal("2026.0.3", Assert.Single(result.Resources).Resource.EplanVersion);
        Assert.Equal("ProjectInfo.xml property 10043", result.Resources[0].Facts.Single(f => f.Name == "eplan_version").Evidence);
        string manifestText = string.Join('\n', result.Resources[0].Facts.Select(f => f.Value + f.Evidence));
        Assert.DoesNotContain("Wang", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("Somewhere", manifestText, StringComparison.Ordinal);
        Assert.Equal(["eplan_compatibility_unchecked", "project_referenced"], result.Findings.Where(f => f.Severity == FindingSeverity.Gap).Select(f => f.Code).Order(StringComparer.Ordinal));
        Assert.Equal(2, result.Findings.Count);
    }

    [Theory]
    [InlineData(null, "no_project_info")]
    [InlineData("<Properties><Property id=\"10043\">2026.0.3", "project_info_unreadable")]
    [InlineData("<!DOCTYPE p [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><Properties>&x;</Properties>", "project_info_unreadable")]
    public async Task Project_MissingOrBrokenProjectInfo_IsAWarning(string? projectInfo, string code)
    {
        WriteProject("In/Plant", projectInfo);

        ScanResult result = await ScanAsync("In");

        Assert.Contains(result.Findings, f => (f.Severity, f.Code) == (FindingSeverity.Warning, code));
        Assert.Null(Assert.Single(result.Resources).Resource.EplanVersion);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task Macro_RootElementGivesTypeAndVersion()
    {
        _folder.Write("In/Pin.ems", "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n<EplanPxfRoot Name=\"Pin\" Type=\"WindowMacro\" Version=\"2.7.11561\" SourceProject=\"D:\\Projects\\Customer.elk\" />");

        ScanResult result = await ScanAsync("In");

        ScannedResource macro = Assert.Single(result.Resources);
        Assert.Equal(("symbol_macro", "2.7.11561"), (macro.Resource.Subtype, macro.Resource.EplanVersion));
        Assert.Equal([("eplan_version", "2.7.11561"), ("pxf_type", "WindowMacro")], Facts(result).Order());
        Assert.DoesNotContain(macro.Facts, f => f.Value.Contains("Customer", StringComparison.Ordinal));
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("In/Page.emp", "<EplanPxfRoot Type=\"Form\" Version=\"2026.0.25347\" />", "pxf_type_mismatch")]
    [InlineData("In/Motor.ema", "not xml at all", "not_eplan_xml")]
    [InlineData("In/Frame.fn1", "<Other Type=\"Frame\" />", "not_eplan_xml")]
    [InlineData("In/F01.f01", "<!DOCTYPE r [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><EplanPxfRoot Type=\"Form\">&x;</EplanPxfRoot>", "not_eplan_xml")]
    public async Task XmlMasterData_OffTheShape_IsAWarning(string path, string content, string code)
    {
        _folder.Write(path, content);

        ScanResult result = await ScanAsync("In");

        Assert.Equal((FindingSeverity.Warning, code), Assert.Single(result.Findings) is var f ? (f.Severity, f.Code) : default);
        Assert.False(result.HasErrors);
    }

    public void Dispose() => _folder.Dispose();

    private void WriteProject(string folder, string? projectInfo)
    {
        string name = Path.GetFileName(folder);
        _folder.Write($"{folder}/{name}.elk", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes($"{name}.edb\n18.0\n")]);
        _folder.Write($"{folder}/{name}.edb/Page.eod", "pages");
        if (projectInfo is not null)
        {
            _folder.Write($"{folder}/{name}.edb/ProjectInfo.xml", projectInfo);
        }
    }

    private static IEnumerable<(string, string)> Facts(ScanResult result) => result.Resources.SelectMany(r => r.Facts).Select(f => (f.Name, f.Value));

    private Task<ScanResult> ScanAsync(string input) => ResourceScanner.ScanAsync([new ScanInput(_folder.PathOf(input))], TestContext.Current.CancellationToken);
}
