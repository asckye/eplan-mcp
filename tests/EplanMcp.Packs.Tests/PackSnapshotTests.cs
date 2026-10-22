using System.Runtime.CompilerServices;
using System.Text;
using DiffEngine;

namespace EplanMcp.Packs.Tests;

/// <summary>
/// A canonical <c>pack.json</c> and <c>pack.lock.json</c> as reviewed snapshots in tests/Contracts.Snapshots, as the
/// server contract is (testing.md 3): a change of the file formats fails here until the new snapshot is reviewed and
/// committed, with a new schemaVersion or lockVersion when old readers could not read the new files. Medium: the
/// snapshots are files.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PackSnapshotTests
{
    /// <summary>A mismatch only fails and writes a .received file; nothing is accepted automatically, no diff tool opens.</summary>
    [ModuleInitializer]
    internal static void Initialize() => DiffRunner.Disabled = true;

    [Fact]
    public Task Manifest_MatchesTheReviewedSnapshot() => Snapshot(PackJson.Serialize(Manifests.Complete()), "pack-manifest");

    [Fact]
    public Task Lock_MatchesTheReviewedSnapshot()
    {
        PackReference Pack(string id, string version, char hash) => new() { Id = id, Version = version, ManifestSha256 = Manifests.Hash(hash) };

        return Snapshot(PackJson.Serialize(PackLock.Create(Pack("acme.cabinet-base", "1.0.0", 'a'), [Pack("acme.parts", "2.3.1", 'c'), Pack("acme.base", "1.4.2", 'b')])), "pack-lock");
    }

    private static SettingsTask Snapshot(byte[] json, string name) =>
        Verify(Encoding.UTF8.GetString(json), "json").UseDirectory("../Contracts.Snapshots").UseFileName(name);
}
