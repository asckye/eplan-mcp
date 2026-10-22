namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class PackVerifierTests : IDisposable
{
    private readonly TempFolder _folder = new();

    [Fact]
    public async Task Check_ValidPack_HasNoProblems()
    {
        PackManifest written = await PackFolders.WriteAsync(_folder);

        PackCheckResult result = await CheckAsync();

        Assert.Empty(result.Problems);
        Assert.Equal(written.Id, result.Manifest?.Id);
        Assert.Equal(4, result.Manifest!.Resources.Sum(r => r.Files));
    }

    [Fact]
    public async Task Check_ChangedFile_IsNamedWithItsHash()
    {
        await PackFolders.WriteAsync(_folder);
        _folder.Write(PackFolders.Macro, "<?xml version=\"1.0\"?><EplanPxfRoot Type=\"WindowMacro\" /> ");

        PackProblem problem = Assert.Single((await CheckAsync()).Problems);

        Assert.Equal(PackFolders.Macro, problem.Location);
        Assert.StartsWith("does not match pack.json", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_FileAddedToAProjectsDataFolder_ChangesTheProject()
    {
        await PackFolders.WriteAsync(_folder);
        _folder.Write("resources/projects/Sample/Sample.edb/Extra.eod", "x");

        PackProblem problem = Assert.Single((await CheckAsync()).Problems);

        Assert.Equal(PackFolders.Project, problem.Location);
    }

    [Fact]
    public async Task Check_SessionFilesChange_ButLockFilesAreNoPartOfAPack()
    {
        await PackFolders.WriteAsync(_folder);
        _folder.Write("resources/projects/Sample/Sample.edb/User.eod", "rewritten by EPLAN on every open");

        Assert.Empty((await CheckAsync()).Problems);

        _folder.Write("resources/projects/Sample/Sample.edb/Page.lck", "lock");
        Assert.Equal(new PackProblem("resources/projects/Sample/Sample.edb/Page.lck", "is in the pack but not in pack.json"), Assert.Single((await CheckAsync()).Problems));
    }

    [Fact]
    public async Task Check_MissingAndUnlistedFiles_AreNamed()
    {
        await PackFolders.WriteAsync(_folder);
        File.Delete(_folder.PathOf(PackFolders.Macro));
        _folder.Write("resources/macros/Unlisted.ema", "x");

        IReadOnlyList<PackProblem> problems = (await CheckAsync()).Problems;

        Assert.Contains(new PackProblem(PackFolders.Macro, "is missing from the pack"), problems);
        Assert.Contains(new PackProblem("resources/macros/Unlisted.ema", "is in the pack but not in pack.json"), problems);
    }

    [Fact]
    public async Task Check_ReferencedResourceInThePack_IsAProblem()
    {
        await PackFolders.WriteAsync(_folder, m => m with
        {
            Resources =
            [
                .. m.Resources.Select(r => r.Path == PackFolders.Macro
                    ? r with { Storage = ResourceStorage.Referenced, Reference = new ResourceReference { Root = "company-macros", Path = "Motor.ema" } }
                    : r),
            ],
        });

        PackProblem problem = Assert.Single((await CheckAsync()).Problems);

        Assert.Equal(new PackProblem(PackFolders.Macro, "is referenced, but its files are in the pack"), problem);
    }

    [Fact]
    public async Task Check_JunctionInThePack_FailsTheCheck()
    {
        await PackFolders.WriteAsync(_folder);
        using var outside = new TempFolder();
        outside.Write("secret.txt", "x");
        _folder.Junction("resources/linked", outside.Root);

        PackProblem problem = Assert.Single((await CheckAsync()).Problems);

        Assert.Equal("'resources/linked' is a symbolic link or junction; packs never follow links.", problem.Message);
    }

    [Fact]
    public async Task Check_FolderWithoutManifest_IsNotAPack()
    {
        _folder.Write("readme.md", "x");

        Assert.Equal(new PackProblem("pack.json", "is missing: the folder is not a pack"), Assert.Single((await CheckAsync()).Problems));
    }

    [Fact]
    public async Task Check_NewerSchemaVersion_IsRefused()
    {
        _folder.Write(PackManifest.FileName, """{ "schemaVersion": 2 }""");

        PackProblem problem = Assert.Single((await CheckAsync()).Problems);

        Assert.StartsWith("pack.json has schemaVersion 2; this EplanMcp reads schemaVersion 1 only.", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_LockOfThisManifest_IsRead()
    {
        PackManifest manifest = await PackFolders.WriteAsync(_folder);
        _folder.Write(PackManifest.LockFileName, PackJson.Serialize(PackLock.Create(Reference(manifest), [])));

        PackCheckResult result = await CheckAsync();

        Assert.Empty(result.Problems);
        Assert.Equal(manifest.Version, result.Lock?.Pack.Version);
    }

    [Fact]
    public async Task Check_LockOfAnotherManifest_IsAProblem()
    {
        PackManifest manifest = await PackFolders.WriteAsync(_folder);
        _folder.Write(PackManifest.LockFileName, PackJson.Serialize(PackLock.Create(Reference(manifest) with { Digest = Manifests.Hash('0') }, [])));

        PackProblem problem = Assert.Single((await CheckAsync()).Problems);

        Assert.Equal("pack.lock.json: pack", problem.Location);
    }

    public void Dispose() => _folder.Dispose();

    private PackReference Reference(PackManifest manifest) => new()
    {
        Id = manifest.Id,
        Version = manifest.Version,
        Digest = PackDigest.Compute(File.ReadAllBytes(_folder.PathOf(PackManifest.FileName))),
    };

    private Task<PackCheckResult> CheckAsync() => PackVerifier.CheckDirectoryAsync(_folder.Root, TestContext.Current.CancellationToken);
}
