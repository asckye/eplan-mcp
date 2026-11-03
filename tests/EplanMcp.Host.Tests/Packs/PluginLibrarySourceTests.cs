using System.Diagnostics;
using System.Text;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class PluginLibrarySourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-libraries-").FullName;
    private HostDataLocations Locations => new(Path.Combine(_root, "data"));
    private string Application => Path.Combine(_root, "app");
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
    private PluginLibrarySource Source => new(Locations, new PluginStateStore(Locations)) { ApplicationDirectory = Application };

    [Fact]
    public async Task LocalAndRegisteredSharedLibraries_AreReadOnly()
    {
        string builtin = WriteIndex(Path.Combine(Application, "plugin-libraries", "builtin"), "builtin");
        string local = WriteIndex(Path.Combine(Locations.PluginLibraries, "local"), "local");
        string shared = Path.Combine(_root, "share");
        string index = WriteIndex(shared, "shared");
        await new PluginStateStore(Locations).WriteAsync(new(1, [], [new("shared", shared)]), null, Cancel);
        byte[][] before = [File.ReadAllBytes(builtin), File.ReadAllBytes(local), File.ReadAllBytes(index)];

        var result = await Source.ReadAsync(Cancel);

        Assert.Empty(result.Warnings);
        Assert.Equal(["builtin", "local", "shared"], result.Libraries.Select(l => l.SourceType));
        Assert.All(result.Libraries, library => Assert.True(library.Readable));
        Assert.Equal(before[0], File.ReadAllBytes(builtin));
        Assert.Equal(before[1], File.ReadAllBytes(local));
        Assert.Equal(before[2], File.ReadAllBytes(index));
        Assert.False(File.Exists(Path.Combine(shared, "entry.eplanpack")));
    }

    [Fact]
    public async Task EveryLocalLibrary_IsDiscovered()
    {
        for (int i = 0; i < 129; i++)
        {
            string name = "library-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            WriteIndex(Path.Combine(Locations.PluginLibraries, name), name);
        }
        var result = await Source.ReadAsync(Cancel);
        Assert.Equal(129, result.Libraries.Count(l => l.SourceType == "local" && l.Readable));
    }

    [Fact]
    public async Task DataRootBuiltin_IsNotTrusted()
    {
        WriteIndex(Path.Combine(Application, "plugin-libraries", "builtin"), "builtin");
        WriteIndex(Path.Combine(Locations.PluginLibraries, "builtin"), "builtin");
        var result = await Source.ReadAsync(Cancel);
        Assert.Equal(["builtin", "local"], result.Libraries.Select(l => l.SourceType));
        Assert.All(result.Libraries, library => Assert.True(library.Readable));
    }

    [Fact]
    public async Task UnreadableLibrary_DoesNotHideOthers()
    {
        WriteIndex(Path.Combine(Locations.PluginLibraries, "good"), "good");
        string bad = WriteIndex(Path.Combine(Locations.PluginLibraries, "bad"), "bad");
        File.WriteAllText(bad, "{bad");
        await new PluginStateStore(Locations).WriteAsync(new(1, [], [new("missing", Path.Combine(_root, "missing"))]), null, Cancel);
        var result = await Source.ReadAsync(Cancel);
        Assert.True(Assert.Single(result.Libraries, l => l.Name == "good").Readable);
        Assert.Equal(3, result.Libraries.Count(l => !l.Readable));
        Assert.All(result.Libraries.Where(l => !l.Readable), l => Assert.NotEmpty(l.Warnings));
        Assert.DoesNotContain(_root, string.Join(" ", result.Libraries.SelectMany(l => l.Warnings)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableState_DoesNotHideLocalLibrary_AndIsReadAgain()
    {
        WriteIndex(Path.Combine(Locations.PluginLibraries, "good"), "good");
        Directory.CreateDirectory(Locations.State);
        File.WriteAllText(Path.Combine(Locations.State, "plugins.json"), "{bad");
        PluginLibrarySource source = Source;
        var first = await source.ReadAsync(Cancel);
        Assert.NotEmpty(first.Warnings);
        Assert.True(Assert.Single(first.Libraries, l => l.Name == "good").Readable);
        File.WriteAllBytes(Path.Combine(Locations.State, "plugins.json"), PackJson.Serialize(PluginState.Empty));
        Assert.Empty((await source.ReadAsync(Cancel)).Warnings);
    }

    [Fact]
    public async Task ReparseEscape_IsRejected_ButRegisteredRootMayBeAJunction()
    {
        string target = Path.Combine(_root, "external");
        WriteIndex(target, "linked");
        Directory.CreateDirectory(Locations.PluginLibraries);
        string link = Path.Combine(Locations.PluginLibraries, "linked");
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        await process.WaitForExitAsync(Cancel);
        Assert.Equal(0, process.ExitCode);
        try
        {
            await new PluginStateStore(Locations).WriteAsync(new(1, [], [new("linked", link)]), null, Cancel);
            var result = await Source.ReadAsync(Cancel);
            Assert.False(Assert.Single(result.Libraries, l => l.Name == "linked" && l.SourceType == "local").Readable);
            Assert.True(Assert.Single(result.Libraries, l => l.Name == "linked" && l.SourceType == "shared").Readable);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task OversizedIndex_IsAWarning_AndCancellationPropagates()
    {
        string file = WriteIndex(Path.Combine(Locations.PluginLibraries, "large"), "large");
        File.WriteAllBytes(file, new byte[PluginLibraryReader.MaxFileBytes + 1]);
        Assert.False(Assert.Single((await Source.ReadAsync(Cancel)).Libraries, l => l.Name == "large").Readable);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Source.ReadAsync(cancelled.Token));
    }

    private static string WriteIndex(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "library.json");
        File.WriteAllText(file, $$"""{"name":"{{name}}","plugins":[{"id":"example","version":"1.0.0","kind":"company","digest":"{{new string('a', 64)}}","file":"entry.eplanpack","title":"Example"}]}""", Encoding.UTF8);
        return file;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
