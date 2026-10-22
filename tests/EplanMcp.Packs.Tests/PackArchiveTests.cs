using System.Text;
using static EplanMcp.Packs.Tests.Zips;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class PackArchiveTests : IDisposable
{
    private readonly TempFolder _pack = new();
    private readonly TempFolder _out = new();

    private string Archive => _out.PathOf("acme.eplanpack");

    private string Destination => _out.PathOf("extracted");

    [Fact]
    public async Task ExportThenExtract_GivesTheSameFilesAndAPassingCheck()
    {
        await PackFolders.WriteAsync(_pack);

        await PackArchive.ExportAsync(_pack.Root, Archive, Cancel);
        PackCheckResult result = await PackArchive.ExtractAsync(Archive, Destination, PackLimits.Default, Cancel);

        Assert.Empty(result.Problems);
        foreach (string relative in new[] { PackManifest.FileName, PackFolders.Macro, "resources/projects/Sample/Sample.edb/Page.eod" })
        {
            Assert.Equal(File.ReadAllBytes(_pack.PathOf(relative)), File.ReadAllBytes(Path.Combine(Destination, relative)));
        }

        Assert.Empty(Directory.GetDirectories(_out.Root, "*.partial-*"));
    }

    [Fact]
    public async Task Export_Twice_GivesTheSameBytes()
    {
        await PackFolders.WriteAsync(_pack);

        await PackArchive.ExportAsync(_pack.Root, Archive, Cancel);
        await PackArchive.ExportAsync(_pack.Root, _out.PathOf("again.eplanpack"), Cancel);

        Assert.Equal(File.ReadAllBytes(Archive), File.ReadAllBytes(_out.PathOf("again.eplanpack")));
    }

    [Fact]
    public async Task Export_OverAnExistingFile_IsRefused()
    {
        await PackFolders.WriteAsync(_pack);
        _out.Write("acme.eplanpack", "keep me");

        var ex = await Assert.ThrowsAsync<PackFormatException>(() => PackArchive.ExportAsync(_pack.Root, Archive, Cancel));

        Assert.Equal("'acme.eplanpack' exists already; EplanMcp never overwrites a file.", ex.Message);
        Assert.Equal("keep me", File.ReadAllText(Archive));
    }

    [Fact]
    public async Task Export_PackWithProblems_IsRefused()
    {
        await PackFolders.WriteAsync(_pack);
        _pack.Write(PackFolders.Macro, "changed");

        await Assert.ThrowsAsync<PackFormatException>(() => PackArchive.ExportAsync(_pack.Root, Archive, Cancel));
        Assert.False(File.Exists(Archive));
    }

    [Fact]
    public async Task Extract_ResourceNotMatchingItsHash_LeavesNoFolder()
    {
        await PackFolders.WriteAsync(_pack);
        string manifest = File.ReadAllText(_pack.PathOf(PackManifest.FileName));
        File.WriteAllBytes(Archive, Bytes(
            new Entry(PackManifest.FileName, manifest),
            new Entry(PackFolders.Macro, "not the macro"),
            new Entry(PackFolders.Project, File.ReadAllText(_pack.PathOf(PackFolders.Project))),
            new Entry("resources/projects/Sample/Sample.edb/ProjectInfo.xml", "<Properties />"),
            new Entry("resources/projects/Sample/Sample.edb/Page.eod", "pages")));

        PackCheckResult result = await PackArchive.ExtractAsync(Archive, Destination, PackLimits.Default, Cancel);

        Assert.Equal(PackFolders.Macro, Assert.Single(result.Problems).Location);
        Assert.False(Directory.Exists(Destination));
        Assert.Empty(Directory.GetDirectories(_out.Root));
    }

    [Fact]
    public async Task Extract_ZipSlip_WritesNothing()
    {
        File.WriteAllBytes(Archive, Bytes(new Entry(PackManifest.FileName, "{}"), new Entry("../evil.txt", "owned")));

        PackCheckResult result = await PackArchive.ExtractAsync(Archive, Destination, PackLimits.Default, Cancel);

        Assert.Equal("../evil.txt", Assert.Single(result.Problems).Location);
        Assert.False(File.Exists(_out.PathOf("evil.txt")));
        Assert.Empty(Directory.GetDirectories(_out.Root));
    }

    [Fact]
    public async Task Extract_UnknownSchemaVersion_IsRefusedBeforeWriting()
    {
        File.WriteAllBytes(Archive, Bytes(new Entry(PackManifest.FileName, """{ "schemaVersion": 7 }"""), new Entry("resources/a.ema")));

        PackCheckResult result = await PackArchive.ExtractAsync(Archive, Destination, PackLimits.Default, Cancel);

        Assert.StartsWith("pack.json has schemaVersion 7;", Assert.Single(result.Problems).Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(_out.Root));
    }

    [Fact]
    public async Task Extract_EntryLargerThanItsHeaderSays_IsCutThereAndFailsTheCheck()
    {
        // The header is what the size limits judge, so no entry may yield more than it declares.
        string bomb = new('a', 100_000);
        PackResource resource = Manifests.File("bomb.txt", ResourceKind.Document) with { Sha256 = TempFolder.Sha256(bomb), Bytes = bomb.Length };
        byte[] zip = Bytes(new Entry(PackManifest.FileName, Encoding.UTF8.GetString(PackJson.Serialize(Manifests.Minimal(resource)))), new Entry("bomb.txt", bomb));
        File.WriteAllBytes(Archive, WithDeclaredSize(zip, "bomb.txt", 10));

        PackCheckResult result = await PackArchive.ExtractAsync(Archive, Destination, PackLimits.Default, Cancel);

        PackProblem problem = Assert.Single(result.Problems);
        Assert.True(
            problem is { Location: "bomb.txt", Message: var m } && m.StartsWith("does not match pack.json: the files have SHA-256 ", StringComparison.Ordinal) && m.Contains(", 10 bytes,", StringComparison.Ordinal)
            || problem.Message.Contains("more bytes than its ZIP header declares", StringComparison.Ordinal)
            || problem.Message.Contains("is damaged", StringComparison.Ordinal),
            problem.ToString());
        Assert.False(Directory.Exists(Destination));
        Assert.Empty(Directory.GetDirectories(_out.Root));
    }

    [Fact]
    public async Task Extract_NotAZip_IsNamed()
    {
        _out.Write("acme.eplanpack", "7z¼¯'\u001c");

        PackCheckResult result = await PackArchive.ExtractAsync(Archive, Destination, PackLimits.Default, Cancel);

        Assert.Equal("'acme.eplanpack' is not a ZIP file, so not an .eplanpack.", Assert.Single(result.Problems).Message);
    }

    [Fact]
    public async Task Extract_IntoAnExistingFolder_IsRefused()
    {
        await PackFolders.WriteAsync(_pack);
        await PackArchive.ExportAsync(_pack.Root, Archive, Cancel);
        Directory.CreateDirectory(Destination);

        PackCheckResult result = await PackArchive.ExtractAsync(Archive, Destination, PackLimits.Default, Cancel);

        Assert.Equal("destination", Assert.Single(result.Problems).Location);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Destination));
    }

    public void Dispose()
    {
        _pack.Dispose();
        _out.Dispose();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
}
