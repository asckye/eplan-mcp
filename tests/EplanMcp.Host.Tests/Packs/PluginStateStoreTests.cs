using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class PluginStateStoreTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-plugin-state-").FullName);
    private PluginStateStore Store => new(_locations);
    private string StateFile => Path.Combine(_locations.State, "plugins.json");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissingFile_IsEmptyWithoutCreatingDirectories()
    {
        (PluginState state, string? revision) = await Store.ReadAsync(Token);

        Assert.Equal(PluginState.Empty, state);
        Assert.Null(revision);
        Assert.False(Directory.Exists(_locations.State));
    }

    [Fact]
    public async Task ChangedOrder_ChangesDigestAndReloadsWithoutCaching()
    {
        PluginState state = new(1, [new("first", "1.0.0"), new("second", "2.0.0")], [new("shared", @"\\server\library")]);
        PluginStateStore store = Store;
        string first = await store.WriteAsync(state, null, Token);
        byte[] bytes = File.ReadAllBytes(StateFile);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), first);
        Assert.Equal(state.Enabled, (await store.ReadAsync(Token)).State.Enabled);
        PluginState reversed = state with { Enabled = [.. state.Enabled.Reverse()] };
        byte[] changed = PackJson.Serialize(reversed);
        File.WriteAllBytes(StateFile, changed);

        (PluginState read, string? revision) = await store.ReadAsync(Token);

        Assert.Equal(reversed.Enabled, read.Enabled);
        Assert.Equal(state.Libraries, read.Libraries);
        Assert.NotEqual(first, revision);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(changed)), revision);
    }

    [Fact]
    public async Task ConcurrentChange_IsNotOverwritten()
    {
        string revision = await Store.WriteAsync(PluginState.Empty, null, Token);
        PluginState next = new(1, [new("first", "1.0.0")], []);
        await Store.WriteAsync(next, revision, Token);
        byte[] before = File.ReadAllBytes(StateFile);

        await Assert.ThrowsAsync<PackFormatException>(() => Store.WriteAsync(PluginState.Empty, revision, Token));

        Assert.Equal(before, File.ReadAllBytes(StateFile));
        Assert.Empty(Directory.GetFiles(_locations.State, "*.partial-*"));
    }

    [Fact]
    public async Task ConcurrentWriters_OnlyOneRevisionWins()
    {
        string revision = await Store.WriteAsync(PluginState.Empty, null, Token);
        async Task<bool> Save(string id)
        {
            try
            { await Store.WriteAsync(new(1, [new(id, "1.0.0")], []), revision, Token); return true; }
            catch (PackFormatException) { return false; }
        }

        bool[] outcomes = await Task.WhenAll(Save("first"), Save("second"));

        Assert.Single(outcomes, outcome => outcome);
        Assert.Single((await Store.ReadAsync(Token)).State.Enabled);
        Assert.Empty(Directory.GetFiles(_locations.State, "*.partial-*"));
    }

    [Fact]
    public async Task BrokenState_ReturnsErrorWithoutUsingStaleState()
    {
        PluginStateStore store = Store;
        await store.WriteAsync(PluginState.Empty, null, Token);
        File.WriteAllText(StateFile, "{");
        await Assert.ThrowsAsync<PackFormatException>(() => store.ReadAsync(Token));
        File.WriteAllBytes(StateFile, new byte[PluginStateStore.MaxFileBytes + 1]);
        await Assert.ThrowsAsync<PackFormatException>(() => store.ReadAsync(Token));
    }

    [Fact]
    public async Task CancelledWrite_PreservesExistingBytes()
    {
        string revision = await Store.WriteAsync(PluginState.Empty, null, Token);
        byte[] before = File.ReadAllBytes(StateFile);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.WriteAsync(new(1, [new("first", "1.0.0")], []), revision, cancelled.Token));

        Assert.Equal(before, File.ReadAllBytes(StateFile));
        Assert.Empty(Directory.GetFiles(_locations.State, "*.partial-*"));
    }

    [Fact]
    public async Task StateDirectoryJunction_IsRejectedForReadsAndWrites()
    {
        string target = Path.Combine(_locations.Root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "plugins.json"), Encoding.UTF8.GetString(PackJson.Serialize(PluginState.Empty)));
        using Process link = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{_locations.State}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        await link.WaitForExitAsync(Token);
        Assert.Equal(0, link.ExitCode);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => Store.ReadAsync(Token));
            await Assert.ThrowsAsync<IOException>(() => Store.WriteAsync(PluginState.Empty, null, Token));
        }
        finally { Directory.Delete(_locations.State); }
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);
}
