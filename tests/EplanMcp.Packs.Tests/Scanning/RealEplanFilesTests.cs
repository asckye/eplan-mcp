using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs.Tests.Scanning;

/// <summary>
/// The scanner on the files EPLAN installs in its public data folder, when they are there (a machine with EPLAN 2026).
/// Reads only: the scanner never writes, and nothing from these files is committed (CLAUDE.md rule 4). Large because it
/// needs an EPLAN installation; CI skips it.
/// </summary>
[Trait("Size", "Large")]
public sealed class RealEplanFilesTests
{
    private const string Data = @"C:\Users\Public\EPLAN\Data";

    [Fact]
    public async Task SampleProjects_PairLinkFileAndDataFolder_AndTellTheirVersion()
    {
        string link = Existing(Path.Combine(Data, "项目", "Company name", "EPLAN_Sample_Project.elk"));

        ScanResult result = await ResourceScanner.ScanAsync([new ScanInput(link)], TestContext.Current.CancellationToken);

        ScannedResource sample = result.Resources.Single(r => r.Resource.Path.EndsWith("/EPLAN_Sample_Project.elk", StringComparison.Ordinal));
        Assert.Equal(ResourceKind.Project, sample.Resource.Kind);
        Assert.True(sample.Resource.Files > 100, $"{sample.Resource.Files} files");
        Assert.StartsWith("2026.", sample.Resource.EplanVersion, StringComparison.Ordinal);
        Assert.Contains(sample.Facts, f => f.Name == "database_version");
        Assert.DoesNotContain(result.Findings, f => f.Severity == FindingSeverity.Error);
        Assert.Contains(result.Findings, f => f.Code == "eplan_compatibility_unchecked" && f.Subject.EndsWith("EPLAN_Sample_Project.elk", StringComparison.Ordinal));
        Assert.Equal(new ResourceReference { Root = "company-name", Path = "EPLAN_Sample_Project.elk" }, sample.Resource.Reference);
    }

    [Fact]
    public async Task BasicProjectsAndMasterData_AreRecognized()
    {
        string basic = Existing(Path.Combine(Data, "模板", "Company name", "GB_bas001.zw9"));
        string symbolMacro = Existing(Path.Combine(Data, "宏", "Company name", "SteckerPinFemale.ems"));

        ScanResult result = await ResourceScanner.ScanAsync([new ScanInput(basic), new ScanInput(symbolMacro)], TestContext.Current.CancellationToken);

        Assert.Contains(result.Resources.SelectMany(r => r.Facts), f => (f.Name, f.Value) == ("archive_format", "7z"));
        Assert.Contains(result.Resources.SelectMany(r => r.Facts), f => (f.Name, f.Value) == ("pxf_type", "WindowMacro"));
        Assert.Equal(["archive_not_inspected"], result.Findings.Select(f => f.Code));
    }

    private static string Existing(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Assert.Skip($"{path} is not on this machine.");
        }

        return path;
    }
}
