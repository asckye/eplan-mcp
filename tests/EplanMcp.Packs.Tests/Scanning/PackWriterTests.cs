using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs.Tests.Scanning;

[Trait("Size", "Medium")]
public sealed class PackWriterTests : IDisposable
{
    private static readonly PackIdentity _identity = new("acme.cabinet-base", "ACME cabinet base", "1.0.0");
    private readonly TempFolder _in = new();
    private readonly TempFolder _out = new();

    private string Target => _out.PathOf("pack");

    [Fact]
    public async Task Write_ScannedInputs_GivesAPackThatChecksClean()
    {
        ScanResult scan = await ScanSampleAsync();
        PackManifest manifest = ManifestGenerator.Generate(scan, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt);

        PackCheckResult result = await PackWriter.WriteAsync(scan, manifest, Target, Cancel);

        Assert.Empty(result.Problems);
        Assert.Equal(File.ReadAllBytes(_in.PathOf("Inputs/Plant/Plant.edb/Page.eod")), File.ReadAllBytes(Path.Combine(Target, "resources/projects/Inputs/Plant/Plant.edb/Page.eod")));
        Assert.Equal(PackJson.Serialize(manifest), File.ReadAllBytes(Path.Combine(Target, PackManifest.FileName)));
        Assert.Equal(manifest.Version, result.Lock?.Pack.Version);
        Assert.Empty(Directory.GetDirectories(_out.Root, "*.partial-*"));
    }

    [Fact]
    public async Task Write_Project_CarriesSessionFilesButNoLockFiles()
    {
        _in.Write("Inputs/Plant/Plant.edb/User.eod", "session");
        _in.Write("Inputs/Plant/Plant.edb/Page.lck", "lock");
        ScanResult scan = await ScanSampleAsync();

        PackCheckResult result = await PackWriter.WriteAsync(scan, ManifestGenerator.Generate(scan, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt), Target, Cancel);

        Assert.Empty(result.Problems);
        Assert.Equal(3, result.Manifest!.Resources.Single(r => r.Kind == ResourceKind.Project).Files);
        Assert.True(File.Exists(Path.Combine(Target, "resources/projects/Inputs/Plant/Plant.edb/User.eod")));
        Assert.False(File.Exists(Path.Combine(Target, "resources/projects/Inputs/Plant/Plant.edb/Page.lck")));
    }

    [Fact]
    public async Task Generate_SameScanAndTime_GivesTheSameManifestBytes()
    {
        ScanResult first = await ScanSampleAsync();
        ScanResult second = await ScanAsync();

        Assert.Equal(
            PackJson.Serialize(ManifestGenerator.Generate(first, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt)),
            PackJson.Serialize(ManifestGenerator.Generate(second, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt)));
    }

    [Fact]
    public async Task Write_FileChangedAfterTheScan_LeavesNoPack()
    {
        ScanResult scan = await ScanSampleAsync();
        _in.Write("Inputs/Macros/Motor.ema", "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" /> changed");

        PackCheckResult result = await PackWriter.WriteAsync(scan, ManifestGenerator.Generate(scan, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt), Target, Cancel);

        Assert.Equal("resources/macros/Inputs/Macros/Motor.ema", Assert.Single(result.Problems).Location);
        Assert.False(Directory.Exists(Target));
        Assert.Empty(Directory.GetDirectories(_out.Root));
    }

    [Fact]
    public async Task Write_ScanWithErrorsOrExistingFolder_IsRefused()
    {
        _in.Write("Inputs/Broken.elk", "Broken.edb\n18.0\n");
        ScanResult broken = await ScanAsync();
        Directory.CreateDirectory(Target);

        await Assert.ThrowsAsync<PackFormatException>(() => PackWriter.WriteAsync(broken, ManifestGenerator.Generate(broken, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt), _out.PathOf("other"), Cancel));
        _in.Write("Inputs/Broken.edb/Page.eod", "pages");
        ScanResult good = await ScanAsync();
        var ex = await Assert.ThrowsAsync<PackFormatException>(() => PackWriter.WriteAsync(good, ManifestGenerator.Generate(good, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt), Target, Cancel));
        Assert.Equal("'pack' exists already; a pack is written into a new folder.", ex.Message);
    }

    public void Dispose()
    {
        _in.Dispose();
        _out.Dispose();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private Task<ScanResult> ScanSampleAsync()
    {
        _in.Write("Inputs/Plant/Plant.elk", "Plant.edb\n18.0\n");
        _in.Write("Inputs/Plant/Plant.edb/ProjectInfo.xml", "<Properties><Property id=\"10043\">2026.0.3</Property></Properties>");
        _in.Write("Inputs/Plant/Plant.edb/Page.eod", "pages");
        _in.Write("Inputs/Macros/Motor.ema", "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />");
        _in.Write("Inputs/parts.csv", "a;b");
        return ScanAsync();
    }

    private Task<ScanResult> ScanAsync() => ResourceScanner.ScanAsync([new ScanInput(_in.PathOf("Inputs"))], Cancel);
}
