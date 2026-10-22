namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class PackHasherTests : IDisposable
{
    private readonly TempFolder _folder = new();

    [Fact]
    public async Task HashTree_IsTheDocumentedFormula()
    {
        _folder.Write("P.elk", "P.edb\n18.0\n");
        _folder.Write("P.edb/b.eod", "bb");
        _folder.Write("P.edb/Sub/a.eod", "a");

        (string sha256, long bytes, int files) = await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(_folder.PathOf("P.elk")), TestContext.Current.CancellationToken);

        string lines = $"P.edb/Sub/a.eod\t1\t{TempFolder.Sha256("a")}\nP.edb/b.eod\t2\t{TempFolder.Sha256("bb")}\nP.elk\t11\t{TempFolder.Sha256("P.edb\n18.0\n")}\n";
        Assert.Equal(TempFolder.Sha256(lines), sha256);
        Assert.Equal(14, bytes);
        Assert.Equal(3, files);
    }

    [Fact]
    public async Task HashTree_LeavesLockAndSessionFilesOut()
    {
        _folder.Write("P.elk", "P.edb\n18.0\n");
        _folder.Write("P.edb/b.eod", "bb");
        (string before, long bytes, int files) = await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(_folder.PathOf("P.elk")), TestContext.Current.CancellationToken);
        _folder.Write("P.edb/b.lck", "lock");
        _folder.Write("P.edb/User.eod", "session");
        _folder.Write("P.edb/GRAPHICS.sdb/user.EOX", "session");

        IReadOnlyList<(string Relative, string FullPath)> project = PackHasher.ProjectFiles(_folder.PathOf("P.elk"));
        (string after, long bytesAfter, int filesAfter) = await PackHasher.HashTreeAsync(project, TestContext.Current.CancellationToken);

        Assert.Equal((before, bytes, files), (after, bytesAfter, filesAfter));
        Assert.Equal(["P.edb/GRAPHICS.sdb/user.EOX", "P.edb/User.eod", "P.edb/b.eod", "P.elk"], project.Select(f => f.Relative).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ProjectFiles_WithoutDataFolder_IsTheLinkFileOnly()
    {
        _folder.Write("P.elk", "P.edb\n18.0\n");

        Assert.Equal(["P.elk"], PackHasher.ProjectFiles(_folder.PathOf("P.elk")).Select(f => f.Relative));
    }

    [Fact]
    public void ProjectFiles_JunctionInTheDataFolder_IsRefused()
    {
        _folder.Write("P.elk", "P.edb\n18.0\n");
        _folder.Write("P.edb/a.eod", "a");
        using var outside = new TempFolder();
        _folder.Junction("P.edb/Linked", outside.Root);

        var ex = Assert.Throws<PackFormatException>(() => PackHasher.ProjectFiles(_folder.PathOf("P.elk")));

        Assert.Equal("'P.edb/Linked' is a symbolic link or junction; packs never follow links.", ex.Message);
    }

    public void Dispose() => _folder.Dispose();
}
