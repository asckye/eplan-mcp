using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>Finding EPLAN versions as doctor does, and the variants installed for each, over a temporary Program Files. Medium.</summary>
[Trait("Size", "Medium")]
public sealed class EplanInstallationTests : IDisposable
{
    private readonly string _programFiles = Directory.CreateTempSubdirectory("eplanmcp-programfiles-").FullName;

    public void Dispose() => Directory.Delete(_programFiles, recursive: true);

    [Fact]
    public void Find_GivesEachVersionWithTheVariantsThatHaveTheirBinForIt()
    {
        string eplan2026 = Put("Platform", "2026.0.3", "Bin", "EPLAN.exe");
        string eplan2025 = Put("Platform", "2025.0.3", "Bin", "EPLAN.exe");
        Put("Pro Panel", "2026.0.3", "Bin", "W3u.exe");
        Put("Electric P8", "2026.0.3", "Bin", "W3u.exe");
        Put("Electric P8", "2025.0.3", "Bin", "W3u.exe");
        Put("Electric P8 Data", "Settings.txt");
        Put("Platform Data", "2026.0.3", "Bin", "readme.txt");
        Put("Harness proD", "2024.0.3", "Bin", "W3u.exe");
        Put("Platform", "2024.0.3", "readme.txt");

        IReadOnlyList<EplanInstallation> found = EplanInstallation.Find(_programFiles, exe => exe == eplan2026 ? "2026.0.3.25421" : null);

        Assert.Equal(
            [(eplan2025, "?", "2025.0.3", "Electric P8"), (eplan2026, "2026.0.3.25421", "2026.0.3", "Electric P8, Pro Panel")],
            found.Select(i => (i.Exe, i.Version, i.Folder, string.Join(", ", i.Variants))));
        Assert.Equal([eplan2025, eplan2026], EplanInstallation.Executables(_programFiles));
    }

    [Fact]
    public void Find_WithoutEplan_IsEmpty()
    {
        Assert.Empty(EplanInstallation.Find(_programFiles, _ => "2026.0.3.25421"));
    }

    private string Put(params string[] parts)
    {
        string file = Path.Combine([_programFiles, "EPLAN", .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Empty);
        return file;
    }
}
