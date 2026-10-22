using EplanMcp.Packs.Knowledge;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs.Tests;

/// <summary>Development packs on synthetic inputs in temporary folders; no EPLAN file is committed (CLAUDE.md rule 4).</summary>
[Trait("Size", "Medium")]
public sealed class DevPackStoreTests : IDisposable
{
    private const string Macro = "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />";
    private static readonly DateTimeOffset _now = Manifests.CreatedAt;
    private readonly TempFolder _in = new();
    private readonly TempFolder _data = new();

    private DevPackStore Store => new(_data.PathOf("packs-dev"));

    private string Pack => Store.FolderOf("acme.panels");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_NewPack_WritesAPackThatChecksClean_WithCandidateDocuments()
    {
        _in.Write("In/Motor.ema", Macro);
        _in.Write("In/guide.md", "# Guide");

        DevPackChange change = await ImportAsync(new ScanInput(_in.PathOf("In")));

        Assert.True(change.Created);
        Assert.Equal(["resources/docs/In/guide.md", "resources/macros/In/Motor.ema"], change.Added.Select(r => r.Path));
        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(Pack, Cancel);
        Assert.Empty(check.Problems);
        Assert.Equal((DevPackStore.ManifestVersion, "ACME panels"), (check.Manifest!.Version, check.Manifest.Name));
        Assert.Equal(KnowledgeStanding.Candidate, Assert.Single(check.Manifest.Docs).Standing);
        Assert.Equal(change.Digest, check.Lock!.Pack.Digest);
        Assert.Empty(Directory.GetDirectories(Store.Root, ".*"));
    }

    [Fact]
    public async Task Import_SameContentAgain_LeavesThePackAsItIs()
    {
        _in.Write("In/Motor.ema", Macro);
        DevPackChange first = await ImportAsync(new ScanInput(_in.PathOf("In")));
        byte[] manifest = File.ReadAllBytes(Path.Combine(Pack, PackManifest.FileName));

        DevPackChange second = await ImportAsync(new ScanInput(_in.PathOf("In")));

        Assert.Equal((false, first.Digest, 0), (second.Created, second.Digest, second.Added.Count));
        Assert.Equal(["resources/macros/In/Motor.ema"], second.Unchanged);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Pack, PackManifest.FileName)));
    }

    [Fact]
    public async Task Import_ChangedContent_IsAConflictUnlessReplaced()
    {
        _in.Write("In/Motor.ema", Macro);
        await ImportAsync(new ScanInput(_in.PathOf("In")));
        _in.Write("In/Motor.ema", Macro.Replace("2026.0.25356", "2026.0.25400", StringComparison.Ordinal));

        var conflict = await Assert.ThrowsAsync<DevPackException>(() => ImportAsync(new ScanInput(_in.PathOf("In"))));
        DevPackChange replaced = await ImportAsync(new ScanInput(_in.PathOf("In")), replace: true);

        Assert.Equal(DevPackError.Conflict, conflict.Error);
        Assert.Equal("resources/macros/In/Motor.ema", Assert.Single(replaced.Replaced).Path);
        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(Pack, Cancel);
        Assert.Empty(check.Problems);
        Assert.Equal("2026.0.25400", Assert.Single(check.Manifest!.Facts, f => f.Name == "eplan_version").Value);
    }

    [Fact]
    public async Task Import_BundledProject_KeepsSessionFilesButNoLockFiles()
    {
        WriteProject("In/Plant");
        _in.Write("In/Plant/Plant.edb/User.eod", "session");
        _in.Write("In/Plant/Plant.edb/User.eox", "session");
        _in.Write("In/Plant/Plant.edb/Page.lck", "lock");

        await ImportAsync(new ScanInput(_in.PathOf("In"), BundleProjects: true));

        string data = Path.Combine(Pack, "resources", "projects", "In", "Plant", "Plant.edb");
        Assert.True(File.Exists(Path.Combine(data, "User.eod")) && File.Exists(Path.Combine(data, "User.eox")));
        Assert.False(File.Exists(Path.Combine(data, "Page.lck")));
        Assert.Empty((await PackVerifier.CheckDirectoryAsync(Pack, Cancel)).Problems);
    }

    [Fact]
    public async Task Import_ProjectBelowAPackSource_IsReferencedThere_AndItsGapIsADocument()
    {
        WriteProject("Share/Refs/Plant");
        var root = new ResourceReference { Root = "company-refs", Path = "Refs" };

        DevPackChange change = await ImportAsync(new ScanInput(_in.PathOf("Share/Refs"), Root: root));

        PackResource project = Assert.Single(change.Added);
        Assert.Equal((ResourceStorage.Referenced, new ResourceReference { Root = "company-refs", Path = "Refs/Plant/Plant.elk" }), (project.Storage, project.Reference));
        Assert.False(Directory.Exists(Path.Combine(Pack, "resources", "projects")));
        string gap = Assert.Single(change.Gaps);
        Assert.StartsWith("knowledge/gap/eplan-compatibility-unchecked-", gap, StringComparison.Ordinal);
        KnowledgeDocument document = KnowledgeDocument.Read(File.ReadAllBytes(Path.Combine(Pack, gap)));
        Assert.Equal((KnowledgeKind.Gap, "project/Refs/Plant/Plant.elk", "open"), (document.Kind, document.Origin!.Asset, (string?)document.Spec["status"]));
        Assert.Equal(ResourceKind.Gap, change.Manifest.Resources.Single(r => r.Path == gap).Kind);
    }

    [Fact]
    public async Task Import_IntoAPackEditedByHand_IsRefusedAndLeavesItAsItWas()
    {
        _in.Write("In/Motor.ema", Macro);
        await ImportAsync(new ScanInput(_in.PathOf("In")));
        File.AppendAllText(Path.Combine(Pack, "resources", "macros", "In", "Motor.ema"), " ");
        byte[] manifest = File.ReadAllBytes(Path.Combine(Pack, PackManifest.FileName));
        _in.Write("In2/Other.ema", Macro);

        var refused = await Assert.ThrowsAsync<DevPackException>(() => ImportAsync(new ScanInput(_in.PathOf("In2"))));

        Assert.Equal(DevPackError.Invalid, refused.Error);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(Pack, PackManifest.FileName)));
        Assert.False(Directory.Exists(Path.Combine(Pack, "resources", "macros", "In2")));
    }

    [Fact]
    public async Task Import_InputsWithErrors_AreRefused()
    {
        _in.Write("In/tool.exe", "MZ");
        ScanResult scan = await ResourceScanner.ScanAsync([new ScanInput(_in.PathOf("In"))], Cancel);

        await Assert.ThrowsAsync<PackFormatException>(() => Store.ImportAsync("acme.panels", "ACME panels", scan, false, "eplanmcp test", _now, Cancel));

        Assert.False(Directory.Exists(Pack));
    }

    public void Dispose()
    {
        _in.Dispose();
        _data.Dispose();
    }

    private async Task<DevPackChange> ImportAsync(ScanInput input, bool replace = false)
    {
        ScanResult scan = await ResourceScanner.ScanAsync([input], Cancel);
        return await Store.ImportAsync("acme.panels", "ACME panels", scan, replace, "eplanmcp test", _now, Cancel);
    }

    private void WriteProject(string folder)
    {
        string name = Path.GetFileName(folder);
        _in.Write($"{folder}/{name}.elk", $"{name}.edb\n18.0\n");
        _in.Write($"{folder}/{name}.edb/ProjectInfo.xml", "<Properties />");
        _in.Write($"{folder}/{name}.edb/Page.eod", "pages");
    }
}
