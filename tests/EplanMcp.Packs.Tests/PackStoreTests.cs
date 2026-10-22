using System.Security.Cryptography;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class PackStoreTests : IDisposable
{
    private readonly TempFolder _data = new();
    private readonly List<TempFolder> _sources = [];

    private PackStore Store => new(_data.PathOf("packs"));

    [Fact]
    public async Task Install_Folder_PutsAReadOnlyCopyUnderIdAndVersion()
    {
        string source = await PackAsync("acme.base", "1.0.0");

        PackInstallResult result = await Store.InstallAsync(source, Cancel);

        Assert.Equal(PackInstallOutcome.Installed, result.Outcome);
        string folder = Store.FolderOf("acme.base", "1.0.0");
        Assert.True((File.GetAttributes(Path.Combine(folder, PackFolders.Macro)) & FileAttributes.ReadOnly) != 0);
        Assert.Empty((await PackVerifier.CheckDirectoryAsync(folder, Cancel)).Problems);
        Assert.Empty(Directory.GetDirectories(Store.Root, ".staging-*"));
    }

    [Fact]
    public async Task Install_EplanpackFile_IsExtractedAndChecked()
    {
        string source = await PackAsync("acme.base", "1.0.0");
        string file = _data.PathOf("acme.base-1.0.0.eplanpack");
        await PackArchive.ExportAsync(source, file, Cancel);

        PackInstallResult result = await Store.InstallAsync(file, Cancel);

        Assert.Equal(PackInstallOutcome.Installed, result.Outcome);
        Assert.Equal(["1.0.0"], Store.Versions("acme.base").Select(v => v.ToString()));
    }

    [Fact]
    public async Task Install_SameVersionAgain_ChangesNothing_AndOtherContentIsRefused()
    {
        string source = await PackAsync("acme.base", "1.0.0");
        await Store.InstallAsync(source, Cancel);
        string before = Sha256(Store.FolderOf("acme.base", "1.0.0"));

        PackInstallResult again = await Store.InstallAsync(source, Cancel);
        PackInstallResult changed = await Store.InstallAsync(await PackAsync("acme.base", "1.0.0", description: "changed"), Cancel);

        Assert.Equal(PackInstallOutcome.AlreadyInstalled, again.Outcome);
        Assert.Equal(PackInstallOutcome.Refused, changed.Outcome);
        Assert.Contains("needs a new version", Assert.Single(changed.Problems).Message, StringComparison.Ordinal);
        Assert.Equal(before, Sha256(Store.FolderOf("acme.base", "1.0.0")));
    }

    [Fact]
    public async Task Install_TwoAtOnce_BothFinishOneAfterTheOther()
    {
        string first = await PackAsync("acme.base", "1.0.0");
        string second = await PackAsync("acme.base", "1.1.0");
        string same = await PackAsync("acme.base", "1.1.0");

        PackInstallResult[] results = await Task.WhenAll(
            Task.Run(() => Store.InstallAsync(first, Cancel), Cancel),
            Task.Run(() => Store.InstallAsync(second, Cancel), Cancel),
            Task.Run(() => Store.InstallAsync(same, Cancel), Cancel));

        Assert.Equal(PackInstallOutcome.Installed, results[0].Outcome);
        Assert.Equal([PackInstallOutcome.Installed, PackInstallOutcome.AlreadyInstalled], results[1..].Select(r => r.Outcome).Order());
        Assert.Equal(["1.1.0", "1.0.0"], Store.Versions("acme.base").Select(v => v.ToString()));
    }

    [Fact]
    public async Task Install_WhileAnotherHoldsTheLock_IsRefusedAfterTheWait()
    {
        string source = await PackAsync("acme.base", "1.0.0");
        Directory.CreateDirectory(Store.Root);
        using var other = new FileStream(Path.Combine(Store.Root, PackStore.LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var store = new PackStore(Store.Root) { LockWait = TimeSpan.FromMilliseconds(300) };

        PackInstallResult result = await store.InstallAsync(source, Cancel);

        Assert.Equal(PackInstallOutcome.Refused, result.Outcome);
        Assert.StartsWith("is being changed by another EplanMcp", Assert.Single(result.Problems).Message, StringComparison.Ordinal);
        Assert.Empty(store.Versions("acme.base"));
        Assert.Empty(Directory.GetDirectories(store.Root));
        await Assert.ThrowsAsync<PackFormatException>(() => store.BindAsync("Plant", "acme.base", "1.0.0", Cancel));
    }

    [Fact]
    public async Task Install_BrokenPack_IsRefusedAndLeavesNothing()
    {
        string source = await PackAsync("acme.base", "1.0.0");
        File.WriteAllText(Path.Combine(source, PackFolders.Macro), "changed");

        PackInstallResult result = await Store.InstallAsync(source, Cancel);

        Assert.Equal(PackInstallOutcome.Refused, result.Outcome);
        Assert.Empty(Directory.GetDirectories(Store.Root));
    }

    [Fact]
    public async Task Binding_SurvivesInstallingANewerVersion()
    {
        await Store.InstallAsync(await PackAsync("acme.base", "1.0.0"), Cancel);
        PackReference pin = await Store.BindAsync(@"D:\Projects\Plant.elk", "acme.base", "1.0.0", Cancel);
        string pinnedContent = Sha256(Store.FolderOf("acme.base", "1.0.0"));

        Assert.Equal(PackInstallOutcome.Installed, (await Store.InstallAsync(await PackAsync("acme.base", "1.1.0"), Cancel)).Outcome);
        (IReadOnlyList<PackReference> packs, IReadOnlyList<PackProblem> problems) = await Store.BoundAsync(@"D:\Projects\Plant.elk", Cancel);

        Assert.Equal([pin], packs);
        Assert.Empty(problems);
        Assert.Equal(pinnedContent, Sha256(Store.FolderOf("acme.base", "1.0.0")));
        Assert.Equal(["1.1.0", "1.0.0"], Store.Versions("acme.base").Select(v => v.ToString()));
    }

    [Fact]
    public async Task Bind_ReplacesThePinOfTheSameId_AndRefusesAnUninstalledVersion()
    {
        await Store.InstallAsync(await PackAsync("acme.base", "1.0.0"), Cancel);
        await Store.InstallAsync(await PackAsync("acme.base", "1.1.0"), Cancel);
        await Store.BindAsync("Plant", "acme.base", "1.0.0", Cancel);

        await Store.BindAsync("Plant", "acme.base", "1.1.0", Cancel);

        Assert.Equal(["1.1.0"], (await Store.BoundAsync("Plant", Cancel)).Packs.Select(p => p.Version));
        await Assert.ThrowsAsync<PackFormatException>(() => Store.BindAsync("Plant", "acme.base", "9.0.0", Cancel));
    }

    [Fact]
    public async Task Lock_TakesTheHighestVersionInRange_ThroughDependencies_Deterministically()
    {
        await Store.InstallAsync(await PackAsync("acme.parts", "2.0.0"), Cancel);
        await Store.InstallAsync(await PackAsync("acme.parts", "2.3.1"), Cancel);
        await Store.InstallAsync(await PackAsync("acme.parts", "3.0.0"), Cancel);
        await Store.InstallAsync(await PackAsync("acme.base", "1.2.0", new PackDependency { Id = "acme.parts", Version = "^2.0.0" }), Cancel);
        PackManifest pack = Manifests.Minimal() with { Id = "acme.line", Dependencies = [new PackDependency { Id = "acme.base", Version = "^1.0.0" }] };

        (PackLock? first, IReadOnlyList<PackProblem> problems) = Store.Lock(pack, Manifests.Hash('9'));
        (PackLock? second, _) = Store.Lock(pack, Manifests.Hash('9'));

        Assert.Empty(problems);
        Assert.Equal([("acme.base", "1.2.0"), ("acme.parts", "2.3.1")], first!.Dependencies.Select(d => (d.Id, d.Version)));
        Assert.Equal(PackJson.Serialize(first), PackJson.Serialize(second));
        Assert.Empty(first.Problems(pack, Manifests.Hash('9')));
    }

    [Fact]
    public async Task Lock_MissingOrConflictingVersions_AreProblems()
    {
        await Store.InstallAsync(await PackAsync("acme.parts", "2.0.0"), Cancel);
        await Store.InstallAsync(await PackAsync("acme.base", "1.0.0", new PackDependency { Id = "acme.parts", Version = "^3.0.0" }), Cancel);
        PackManifest pack = Manifests.Minimal() with
        {
            Id = "acme.line",
            Dependencies = [new PackDependency { Id = "acme.parts", Version = "^2.0.0" }, new PackDependency { Id = "acme.base", Version = "^1.0.0" }, new PackDependency { Id = "acme.none", Version = "*" }],
        };

        (PackLock? locked, IReadOnlyList<PackProblem> problems) = Store.Lock(pack, Manifests.Hash('9'));

        Assert.Null(locked);
        Assert.Equal(["acme.none", "acme.parts"], problems.Select(p => p.Location).Order(StringComparer.Ordinal));
    }

    public void Dispose()
    {
        _data.Dispose();
        _sources.ForEach(s => s.Dispose());
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<string> PackAsync(string id, string version, PackDependency? dependency = null, string? description = null)
    {
        var folder = new TempFolder();
        _sources.Add(folder);
        await PackFolders.WriteAsync(folder, m => m with { Id = id, Version = version, Description = description, Dependencies = dependency is null ? [] : [dependency] });
        return folder.Root;
    }

    private static string Sha256(string folder) =>
        string.Join(',', Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))));
}
