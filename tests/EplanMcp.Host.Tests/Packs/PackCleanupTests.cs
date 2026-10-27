using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class PackCleanupTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-pack-cleanup-").FullName);
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero));
    private PackLibrary Library => new(_locations, HostConfig.Defaults(_locations), new AuditLog(_locations.Audit, _clock), _clock);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("json")]
    [InlineData("unknown_member")]
    [InlineData("duplicate")]
    [InlineData("finite")]
    [InlineData("locked_file")]
    public async Task Remove_UnreadableBinding_BlocksUnoccupiedVersionWithoutChangingFiles(string failure)
    {
        PackReference unused = WritePack("unused");
        Seed(Binding(1) with { Project = "other" });
        string binding = Seed(Binding(2));
        switch (failure)
        {
            case "json":
                File.WriteAllText(binding, "{");
                break;
            case "unknown_member":
                File.WriteAllText(binding, File.ReadAllText(binding).Replace("\"revision\"", "\"unknown\"", StringComparison.Ordinal));
                break;
            case "duplicate":
                File.WriteAllText(binding, File.ReadAllText(binding).Replace("\"revision\": 0", "\"revision\": 0, \"revision\": 1", StringComparison.Ordinal));
                break;
            case "finite":
                File.WriteAllText(binding, File.ReadAllText(binding).Replace("\"projectValues\": {}", "\"projectValues\": {\"power\":{\"default\":1e100}}", StringComparison.Ordinal));
                break;
        }
        byte[] before = File.ReadAllBytes(binding);
        using FileStream? held = failure == "locked_file" ? new FileStream(binding, FileMode.Open, FileAccess.Read, FileShare.None) : null;

        PackFormatException error = await Assert.ThrowsAsync<PackFormatException>(() => Library.RemoveUnusedVersionAsync(unused.Id, unused.Version, Token));

        Assert.StartsWith("/bindings:", error.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Library.Installed.FolderOf(unused.Id, unused.Version)));
        held?.Dispose();
        Assert.Equal(before, File.ReadAllBytes(binding));
        Assert.Empty(Records());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Remove_DirectOrTransitiveLock_RefusesVersion(int version, bool transitive)
    {
        PackReference leaf = WritePack("leaf");
        PackReference root = WritePack("root", dependencies: [leaf]);
        string binding = Seed(Binding(version) with { Locks = [transitive ? root : leaf] });
        byte[] before = File.ReadAllBytes(binding);

        PackFormatException error = await Assert.ThrowsAsync<PackFormatException>(() => Library.RemoveUnusedVersionAsync(leaf.Id, leaf.Version, Token));

        Assert.StartsWith("/locks:", error.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Library.Installed.FolderOf(leaf.Id, leaf.Version)));
        Assert.Equal(before, File.ReadAllBytes(binding));
        Assert.Empty(Records());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("digest")]
    [InlineData("closure")]
    public async Task Remove_UnverifiableClosure_BlocksUnrelatedDeletion(string failure)
    {
        PackReference leaf = WritePack("leaf");
        PackReference root = WritePack("root", dependencies: [leaf]);
        PackReference unused = WritePack("unused");
        Seed(Binding(1) with { Locks = [root] });
        if (failure == "missing")
        {
            File.Delete(Path.Combine(Library.Installed.FolderOf(root.Id, root.Version), PackManifest.LockFileName));
        }
        else if (failure == "digest")
        {
            string file = Path.Combine(Library.Installed.FolderOf(leaf.Id, leaf.Version), PackManifest.FileName);
            File.WriteAllBytes(file, PackJson.Serialize(PackJson.ReadManifest(File.ReadAllBytes(file)) with { Name = "Changed" }));
        }
        else
        {
            PackReference other = WritePack("other");
            string file = Path.Combine(Library.Installed.FolderOf(root.Id, root.Version), PackManifest.LockFileName);
            File.WriteAllBytes(file, PackJson.Serialize(PackLock.Create(root, [leaf, other])));
        }

        await Assert.ThrowsAsync<PackFormatException>(() => Library.RemoveUnusedVersionAsync(unused.Id, unused.Version, Token));

        Assert.True(Directory.Exists(Library.Installed.FolderOf(unused.Id, unused.Version)));
        Assert.Empty(Records());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Remove_OnlyNewVersionIsUnused_DeletesItAndPreservesExistingLocks(int version)
    {
        PackReference old = WritePack("root");
        string oldFolder = Library.Installed.FolderOf(old.Id, old.Version);
        string binding = Seed(Binding(version) with { Locks = [old] });
        byte[] before = File.ReadAllBytes(binding);
        byte[] lockBefore = File.ReadAllBytes(Path.Combine(oldFolder, PackManifest.LockFileName));
        WritePack("root", "2.0.0", source: true);
        Assert.Equal(PackInstallOutcome.Installed, (await Library.Installed.InstallAsync(Path.Combine(_locations.Root, "source"), Token)).Outcome);
        Assert.Equal(before, File.ReadAllBytes(binding));
        Assert.Equal(lockBefore, File.ReadAllBytes(Path.Combine(oldFolder, PackManifest.LockFileName)));

        await Library.RemoveUnusedVersionAsync("root", "2.0.0", Token);

        Assert.False(Directory.Exists(Library.Installed.FolderOf("root", "2.0.0")));
        Assert.True(Directory.Exists(oldFolder));
        Assert.Equal(before, File.ReadAllBytes(binding));
        Assert.Equal(lockBefore, File.ReadAllBytes(Path.Combine(oldFolder, PackManifest.LockFileName)));
        Assert.Equal(["started", "committed"], Records().Select(r => r["phase"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Remove_LegacyLeafWithoutLockFile_StillProtectsItsPinnedVersion()
    {
        PackReference legacy = WritePack("legacy");
        Seed(Binding(1) with { Locks = [legacy] });
        File.Delete(Path.Combine(Library.Installed.FolderOf(legacy.Id, legacy.Version), PackManifest.LockFileName));
        PackReference unused = WritePack("unused");

        await Library.RemoveUnusedVersionAsync(unused.Id, unused.Version, Token);
        PackFormatException error = await Assert.ThrowsAsync<PackFormatException>(() => Library.RemoveUnusedVersionAsync(legacy.Id, legacy.Version, Token));

        Assert.StartsWith("/locks:", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Library.Installed.FolderOf(unused.Id, unused.Version)));
        Assert.True(Directory.Exists(Library.Installed.FolderOf(legacy.Id, legacy.Version)));
    }

    [Fact]
    public async Task Remove_AuditUnavailable_PreservesUnusedVersion()
    {
        PackReference unused = WritePack("unused");
        File.WriteAllText(_locations.Audit, "blocked");

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => Library.RemoveUnusedVersionAsync(unused.Id, unused.Version, Token));

        Assert.Equal(ErrorReason.AuditUnavailable, error.Error.Reason);
        Assert.True(Directory.Exists(Library.Installed.FolderOf(unused.Id, unused.Version)));
    }

    [Fact]
    public async Task Remove_WaitsForStoreLock_ThenSeesPublishedBinding()
    {
        PackReference pin = WritePack("root");
        using var held = new FileStream(Path.Combine(_locations.Packs, PackStore.LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Task cleanup = Library.RemoveUnusedVersionAsync(pin.Id, pin.Version, Token);
        Assert.False(cleanup.IsCompleted);
        Seed(Binding(2) with { Locks = [pin] });
        held.Dispose();

        PackFormatException error = await Assert.ThrowsAsync<PackFormatException>(() => cleanup);

        Assert.StartsWith("/locks:", error.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Library.Installed.FolderOf(pin.Id, pin.Version)));
    }

    [Theory]
    [InlineData("../outside", "1.0.0")]
    [InlineData("root", "../outside")]
    public async Task Remove_InvalidIdentity_RefusesBeforeDeletion(string id, string version)
    {
        PackReference pin = WritePack("root");

        await Assert.ThrowsAsync<PackFormatException>(() => Library.RemoveUnusedVersionAsync(id, version, Token));

        Assert.True(Directory.Exists(Library.Installed.FolderOf(pin.Id, pin.Version)));
    }

    private static ProjectBinding Binding(int version) => new()
    {
        BindingVersion = version,
        Project = "plant",
        Revision = 0,
        ProjectValues = version == 2 ? new() : null,
        PackRoots = version == 2 ? [] : null,
    };

    private string Seed(ProjectBinding binding)
    {
        string folder = Directory.CreateDirectory(Library.Workspaces.FolderOf(binding.Project)).FullName;
        string file = Path.Combine(folder, ProjectBinding.FileName);
        File.WriteAllBytes(file, PackJson.Serialize(binding));
        return file;
    }

    private PackReference WritePack(string id, string version = "1.0.0", PackReference[]? dependencies = null, bool source = false)
    {
        string folder = Directory.CreateDirectory(source ? Path.Combine(_locations.Root, "source") : Library.Installed.FolderOf(id, version)).FullName;
        var manifest = new PackManifest
        {
            SchemaVersion = 2,
            Id = id,
            Name = id,
            Version = version,
            Compatibility = new(),
            Resources = [],
            Dependencies = [.. (dependencies ?? []).Select(p => new PackDependency { Id = p.Id, Version = p.Version })],
            Provenance = new() { CreatedAt = _clock.GetUtcNow(), CreatedBy = "eplanmcp test", Inputs = [] },
        };
        byte[] bytes = PackJson.Serialize(manifest);
        var pin = new PackReference { Id = id, Version = version, Digest = PackDigest.Compute(bytes) };
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(pin, dependencies ?? [])));
        return pin;
    }

    private List<JsonObject> Records() => Directory.Exists(_locations.Audit)
        ? [.. Directory.GetFiles(_locations.Audit, "*.jsonl").SelectMany(File.ReadLines).Select(line => JsonNode.Parse(line)!.AsObject())] : [];

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_locations.Root, "*", SearchOption.AllDirectories))
        { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(_locations.Root, recursive: true);
    }
}
