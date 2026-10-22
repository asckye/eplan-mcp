namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class WorkspaceStoreTests : IDisposable
{
    private const string Plant = @"D:\Projects\Plant.elk";
    private readonly TempFolder _data = new();
    private readonly List<TempFolder> _sources = [];

    private PackStore Packs => new(_data.PathOf("packs"));

    private WorkspaceStore Workspaces => new(_data.PathOf("workspaces"), Packs);

    [Fact]
    public async Task Lock_SurvivesInstallingANewerVersion()
    {
        await InstallAsync("acme.base", "1.0.0");
        ProjectBinding locked = await Workspaces.LockPackAsync(Plant, "acme.base", "1.0.0", Cancel);

        await InstallAsync("acme.base", "1.1.0");
        ProjectBinding? read = await Workspaces.ReadAsync(Plant, Cancel);

        Assert.Equal(locked.Locks, read!.Locks);
        Assert.Equal(("1.0.0", Packs.InstalledDigest("acme.base", "1.0.0")), (read.Locks[0].Version, read.Locks[0].Digest));
        Assert.Empty(await Workspaces.CheckAsync(Plant, Cancel));
        Assert.Equal((1, Plant, 1), (read.BindingVersion, read.Project, read.Revision));
    }

    [Fact]
    public async Task Lock_ReplacesTheSameIdAndRaisesTheRevision_AndRefusesWhatIsNotInstalled()
    {
        await InstallAsync("acme.base", "1.0.0");
        await InstallAsync("acme.base", "1.1.0");
        await Workspaces.LockPackAsync(Plant, "acme.base", "1.0.0", Cancel);

        ProjectBinding binding = await Workspaces.LockPackAsync(Plant, "acme.base", "1.1.0", Cancel);

        Assert.Equal((2, "1.1.0"), (binding.Revision, Assert.Single(binding.Locks).Version));
        await Assert.ThrowsAsync<PackFormatException>(() => Workspaces.LockPackAsync(Plant, "acme.base", "9.0.0", Cancel));
    }

    [Fact]
    public async Task Folder_IsOnePerProjectKey_IgnoringCase()
    {
        await InstallAsync("acme.base", "1.0.0");
        await Workspaces.LockPackAsync(Plant, "acme.base", "1.0.0", Cancel);

        Assert.Equal(Workspaces.FolderOf(Plant), Workspaces.FolderOf(Plant.ToLowerInvariant()));
        Assert.NotEqual(Workspaces.FolderOf(Plant), Workspaces.FolderOf(@"D:\Projects\Other.elk"));
        Assert.Null(await Workspaces.ReadAsync(@"D:\Projects\Other.elk", Cancel));
        Assert.True(File.Exists(Path.Combine(Workspaces.FolderOf(Plant), ProjectBinding.FileName)));
    }

    [Fact]
    public async Task Check_LockedVersionGoneOrChanged_IsAProblem()
    {
        await InstallAsync("acme.base", "1.0.0");
        await Workspaces.LockPackAsync(Plant, "acme.base", "1.0.0", Cancel);
        string manifest = Path.Combine(Packs.FolderOf("acme.base", "1.0.0"), PackManifest.FileName);
        File.SetAttributes(manifest, FileAttributes.Normal);
        File.AppendAllText(manifest, "\n");
        Assert.Empty(await Workspaces.CheckAsync(Plant, Cancel));

        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"name\": \"ACME cabinet base\"", "\"name\": \"Changed\"", StringComparison.Ordinal));

        Assert.Equal("acme.base 1.0.0", Assert.Single(await Workspaces.CheckAsync(Plant, Cancel)).Location);
    }

    [Fact]
    public async Task Lock_WhileAnotherHoldsTheWorkspace_Throws()
    {
        await InstallAsync("acme.base", "1.0.0");
        var workspaces = new WorkspaceStore(_data.PathOf("workspaces"), Packs) { LockWait = TimeSpan.FromMilliseconds(200) };
        Directory.CreateDirectory(workspaces.FolderOf(Plant));
        using var other = new FileStream(Path.Combine(workspaces.FolderOf(Plant), ".binding.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var ex = await Assert.ThrowsAsync<PackFormatException>(() => workspaces.LockPackAsync(Plant, "acme.base", "1.0.0", Cancel));

        Assert.StartsWith("The project's binding is being changed by another EplanMcp", ex.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _data.Dispose();
        _sources.ForEach(s => s.Dispose());
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task InstallAsync(string id, string version)
    {
        var folder = new TempFolder();
        _sources.Add(folder);
        await PackFolders.WriteAsync(folder, m => m with { Id = id, Version = version });
        Assert.Equal(PackInstallOutcome.Installed, (await Packs.InstallAsync(folder.Root, Cancel)).Outcome);
    }
}
