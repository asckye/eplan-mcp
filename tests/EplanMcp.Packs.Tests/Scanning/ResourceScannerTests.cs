using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs.Tests.Scanning;

/// <summary>Synthetic inputs in a temporary folder; no EPLAN file or customer data is committed (CLAUDE.md rule 4).</summary>
[Trait("Size", "Medium")]
public sealed class ResourceScannerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    [Fact]
    public async Task Scan_MixedFolder_ClassifiesEveryFile()
    {
        _folder.Write("Inputs/Plant/Plant.elk", "Plant.edb\n18.0\n");
        _folder.Write("Inputs/Plant/Plant.edb/ProjectInfo.xml", "<Properties />");
        _folder.Write("Inputs/Plant/Plant.edb/Sub/Page.eod", "pages");
        _folder.Write("Inputs/Macros/Motor.ema", Macro);
        _folder.Write("Inputs/Forms/F01_001.f01", "<EplanPxfRoot Type=\"Form\" />");
        _folder.Write("Inputs/parts.csv", "a;b");
        _folder.Write("Inputs/readme.md", "# readme");
        _folder.Write("Inputs/notes.xyz", "?");

        ScanResult result = await ScanAsync(new ScanInput(_folder.PathOf("Inputs")));

        Assert.Equal(
            [
                ("resources/docs/Inputs/readme.md", ResourceKind.Document, 1),
                ("resources/forms/Inputs/Forms/F01_001.f01", ResourceKind.Form, 1),
                ("resources/macros/Inputs/Macros/Motor.ema", ResourceKind.Macro, 1),
                ("resources/mappings/Inputs/parts.csv", ResourceKind.MaterialMapping, 1),
                ("resources/other/Inputs/notes.xyz", ResourceKind.Other, 1),
                ("resources/projects/Inputs/Plant/Plant.elk", ResourceKind.Project, 3),
            ],
            result.Resources.Select(r => (r.Resource.Path, r.Resource.Kind, r.Resource.Files)));
        Assert.Equal(["eplan_compatibility_unchecked", "kind_by_extension", "unknown_kind"], result.Findings.Select(f => f.Code).Order(StringComparer.Ordinal));
        Assert.False(result.HasErrors);
        Assert.Equal([new PackInput { Name = "Inputs", Kind = PackInputKind.Folder }], result.Inputs);

        ScannedResource project = result.Resources.Single(r => r.Resource.Kind == ResourceKind.Project);
        Assert.Equal(new ResourceSource { Input = "Inputs", Path = "Plant/Plant.elk" }, project.Resource.Source);
        Assert.Equal((await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(_folder.PathOf("Inputs/Plant/Plant.elk")), Cancel)).Sha256, project.Resource.Sha256);
        Assert.Equal("window_macro", result.Resources.Single(r => r.Resource.Kind == ResourceKind.Macro).Resource.Subtype);
    }

    [Fact]
    public async Task Scan_LinkFileWithoutDataFolder_IsAnError()
    {
        _folder.Write("Inputs/Plant.elk", "Plant.edb\n18.0\n");

        ScanResult result = await ScanAsync(new ScanInput(_folder.PathOf("Inputs")));

        ScanFinding finding = Assert.Single(result.Findings);
        Assert.Equal((FindingSeverity.Error, "missing_data_folder", "Inputs/Plant.elk"), (finding.Severity, finding.Code, finding.Subject));
        Assert.Contains("'Plant.edb'", finding.Message, StringComparison.Ordinal);
        Assert.Empty(result.Resources);
    }

    [Fact]
    public async Task Scan_DataFolderWithoutLinkFile_IsAnError()
    {
        _folder.Write("Inputs/Plant.edb/Page.eod", "pages");

        ScanResult inFolder = await ScanAsync(new ScanInput(_folder.PathOf("Inputs")));
        ScanResult alone = await ScanAsync(new ScanInput(_folder.PathOf("Inputs/Plant.edb")));

        Assert.Equal(("missing_link_file", "Inputs/Plant.edb"), Assert.Single(inFolder.Findings) is var f ? (f.Code, f.Subject) : default);
        Assert.Equal(("missing_link_file", "Plant.edb"), Assert.Single(alone.Findings) is var g ? (g.Code, g.Subject) : default);
        Assert.Empty(inFolder.Resources);
    }

    [Fact]
    public async Task Scan_LinkFileGivenAlone_TakesItsDataFolder()
    {
        _folder.Write("Projects/Plant.elk", "Plant.edb\n18.0\n");
        _folder.Write("Projects/Plant.edb/Page.eod", "pages");

        ScanResult result = await ScanAsync(new ScanInput(_folder.PathOf("Projects/Plant.elk")));

        ScannedResource project = Assert.Single(result.Resources);
        Assert.Equal(("resources/projects/Plant.elk", 2), (project.Resource.Path, project.Resource.Files));
        Assert.Equal(["Plant.edb/Page.eod", "Plant.elk"], project.FilesOnDisk.Select(f => f.Relative).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scan_EmptyMissingAndLockedFiles_AreErrors()
    {
        _folder.Write("Inputs/empty.ema", "");
        string locked = _folder.Write("Inputs/locked.ema", Macro);
        using var holder = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ScanResult result = await ScanAsync(new ScanInput(_folder.PathOf("Inputs")), new ScanInput(_folder.PathOf("missing")));

        Assert.Equal(
            [("empty_file", "Inputs/empty.ema"), ("input_missing", "missing"), ("unreadable", "Inputs/locked.ema")],
            result.Findings.Select(f => (f.Code, f.Subject)).Order());
        Assert.True(result.HasErrors);
        Assert.DoesNotContain(result.Findings, f => f.Message.Contains(_folder.Root, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scan_Junctions_AreNeverFollowed()
    {
        using var outside = new TempFolder();
        outside.Write("secret.ema", Macro);
        _folder.Write("Inputs/Plant.elk", "Plant.edb\n18.0\n");
        _folder.Write("Inputs/Plant.edb/Page.eod", "pages");
        _folder.Junction("Inputs/Linked", outside.Root);
        _folder.Junction("Inputs/Plant.edb/Linked", outside.Root);

        ScanResult result = await ScanAsync(new ScanInput(_folder.PathOf("Inputs")));

        Assert.Equal([("link_skipped", "Inputs/Linked"), ("link_skipped", "Inputs/Plant.elk")], result.Findings.Select(f => (f.Code, f.Subject)).Order());
        Assert.Empty(result.Resources);
    }

    [Fact]
    public async Task Scan_SameNames_GetUniqueInputsAndCollidingPathsAreErrors()
    {
        _folder.Write("a/Data/Motor.ema", Macro);
        _folder.Write("b/Data/Motor.ema", Macro);

        ScanResult folders = await ScanAsync(new ScanInput(_folder.PathOf("a/Data")), new ScanInput(_folder.PathOf("b/Data")));
        ScanResult files = await ScanAsync(new ScanInput(_folder.PathOf("a/Data/Motor.ema")), new ScanInput(_folder.PathOf("b/Data/Motor.ema")));

        Assert.Equal(["resources/macros/Data-2/Motor.ema", "resources/macros/Data/Motor.ema"], folders.Resources.Select(r => r.Resource.Path));
        Assert.Equal(["Data", "Data-2"], folders.Inputs.Select(i => i.Name));
        Assert.Equal("duplicate_path", Assert.Single(files.Findings).Code);
    }

    [Fact]
    public async Task Scan_GivenKind_AppliesToNonEplanFilesOnly()
    {
        _folder.Write("Rules/naming.md", "Device tags start with -");
        _folder.Write("Rules/Motor.ema", Macro);

        ScanResult result = await ScanAsync(new ScanInput(_folder.PathOf("Rules"), ResourceKind.Rule));

        Assert.Equal(
            [("resources/macros/Rules/Motor.ema", ResourceKind.Macro), ("resources/rules/Rules/naming.md", ResourceKind.Rule)],
            result.Resources.Select(r => (r.Resource.Path, r.Resource.Kind)));
        Assert.Empty(result.Findings);
    }

    private const string Macro = "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />";

    public void Dispose() => _folder.Dispose();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Task<ScanResult> ScanAsync(params ScanInput[] inputs) => ResourceScanner.ScanAsync(inputs, Cancel);
}
