using EplanMcp.Workbench.Mcp;

namespace EplanMcp.Workbench.Tests.Mcp;

/// <summary>The MCP page's version tiles read only the installer's folders, per year and variant.</summary>
[Trait("Size", "Small")]
public sealed class EplanInstallsTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-installs-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void TheYearsAndVariants_AreReadFromTheInstallFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Electric P8", "2025.0.3"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Pro Panel", "2026.0.3"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Electric P8", "2026.0.3"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "Platform", "2027.0.0")); // the platform alone is no variant

        IReadOnlyList<EplanInstall> installs = EplanInstalls.Scan(_root.FullName);

        Assert.Equal([2024, 2025, 2026, 2027], installs.Select(i => i.Year));
        Assert.Equal([[], ["Electric P8"], ["Electric P8", "Pro Panel"], []], installs.Select(i => i.Variants));
        Assert.Equal([false, true, true, false], installs.Select(i => i.Installed));
    }

    [Fact]
    public void AMissingRoot_MeansNothingIsInstalled()
    {
        Assert.All(EplanInstalls.Scan(Path.Combine(_root.FullName, "absent")), i => Assert.False(i.Installed));
    }
}
