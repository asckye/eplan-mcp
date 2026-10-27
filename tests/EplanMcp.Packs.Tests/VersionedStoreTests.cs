using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class VersionedStoreTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Stores_V1AndV2_RoundTripWithoutRewritingInstalledFilesOrBindings(int version)
    {
        using var data = new TempFolder();
        using var source = new TempFolder();
        CancellationToken cancel = TestContext.Current.CancellationToken;
        KnowledgeKind kind = version == 1 ? KnowledgeKind.Module : KnowledgeKind.ConfigLayer;
        var knowledge = new KnowledgeDocument
        {
            ApiVersion = version == 1 ? KnowledgeDocument.CurrentApiVersion : KnowledgeDocument.V2ApiVersion,
            Kind = kind,
            Metadata = new KnowledgeMetadata { Name = "motor" },
            Spec = JsonNode.Parse(version == 1 ? """{"macro":"macro/motor","parameters":[]}""" : """{"layer":"company","schema":"config_schema/motors","settings":{"motor.power":{"default":7.5}}}""")!.AsObject(),
        };
        string path = KnowledgeDocument.PathOf(kind, "motor");
        byte[] documentBytes = PackJson.Serialize(knowledge);
        source.Write(path, documentBytes);
        PackResource resource = await PackFolders.ResourceAsync(source, path, KnowledgeDocument.ResourceKindOf(kind));
        var resources = new List<PackResource> { resource };
        if (version == 2)
        {
            source.Write(ConfigFixtures.SchemaPath, ConfigFixtures.Read("MotorsPack/" + ConfigFixtures.SchemaPath));
            resources.Add(await PackFolders.ResourceAsync(source, ConfigFixtures.SchemaPath, ResourceKind.ConfigSchema));
        }

        PackManifest manifest = await PackFolders.WriteAsync(source, m => m with { SchemaVersion = version, Resources = [.. resources.Concat(m.Resources).OrderBy(r => r.Path, StringComparer.Ordinal)] });
        byte[] manifestBytes = File.ReadAllBytes(source.PathOf(PackManifest.FileName));
        string digest = PackDigest.Compute(manifestBytes);
        byte[] lockBytes = PackJson.Serialize(PackLock.Create(new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = digest }, []));
        source.Write(PackManifest.LockFileName, lockBytes);
        var packs = new PackStore(data.PathOf("packs"));
        var workspaces = new WorkspaceStore(data.PathOf("workspaces"), packs);

        Assert.Equal(PackInstallOutcome.Installed, (await packs.InstallAsync(source.Root, cancel)).Outcome);
        ProjectBinding binding = version == 1
            ? await workspaces.LockPackAsync("plant", manifest.Id, manifest.Version, cancel)
            : PackJson.ReadBinding(Encoding.UTF8.GetBytes(VersionedReadModelTests.Binding)) with
            {
                PackRoots = [new PackDependency { Id = manifest.Id, Version = manifest.Version }],
                Locks = [new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = digest }],
            };
        if (version == 2)
        {
            binding = binding with { Cabinets = [binding.Cabinets[0] with { Layers = [AssetNames.Id(manifest.Id, manifest.Version, "config_layer/motor")] }] };
        }

        byte[] bindingBytes = PackJson.Serialize(binding);
        string bindingFile = Path.Combine(workspaces.FolderOf("plant"), ProjectBinding.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(bindingFile)!);
        File.WriteAllBytes(bindingFile, bindingBytes);

        ProjectBinding? read = await workspaces.ReadAsync("plant", cancel);
        Assert.Equal(bindingBytes, PackJson.Serialize(read));
        Assert.Empty(await workspaces.CheckAsync("plant", cancel));
        Assert.Equal((1, 0), ((await workspaces.AllAsync(cancel)).Bindings.Count, (await workspaces.AllAsync(cancel)).Unreadable));
        string installed = packs.FolderOf(manifest.Id, manifest.Version);
        Assert.Equal(manifestBytes, File.ReadAllBytes(Path.Combine(installed, PackManifest.FileName)));
        Assert.Equal(lockBytes, File.ReadAllBytes(Path.Combine(installed, PackManifest.LockFileName)));
        Assert.Equal(documentBytes, PackJson.Serialize(KnowledgeDocument.Read(File.ReadAllBytes(Path.Combine(installed, path)))));

        string archive = data.PathOf("exchange.eplanpack");
        await PackArchive.ExportAsync(installed, archive, cancel);
        var imported = new PackStore(data.PathOf("imported"));
        Assert.Equal(PackInstallOutcome.Installed, (await imported.InstallAsync(archive, cancel)).Outcome);
        Assert.Equal(manifestBytes, File.ReadAllBytes(Path.Combine(imported.FolderOf(manifest.Id, manifest.Version), PackManifest.FileName)));
        Assert.Equal(documentBytes, File.ReadAllBytes(Path.Combine(imported.FolderOf(manifest.Id, manifest.Version), path)));
        Assert.Equal(PackInstallOutcome.AlreadyInstalled, (await packs.InstallAsync(source.Root, cancel)).Outcome);

        using var newer = new TempFolder();
        await PackFolders.WriteAsync(newer, m => m with { Id = manifest.Id, Version = "2.0.0", SchemaVersion = 2 });
        Assert.Equal(PackInstallOutcome.Installed, (await packs.InstallAsync(newer.Root, cancel)).Outcome);
        Assert.Equal(bindingBytes, File.ReadAllBytes(bindingFile));
        Assert.Equal(manifestBytes, File.ReadAllBytes(Path.Combine(installed, PackManifest.FileName)));
        Assert.Equal(lockBytes, File.ReadAllBytes(Path.Combine(installed, PackManifest.LockFileName)));

        if (version == 1)
        {
            await workspaces.LockPackAsync("plant", manifest.Id, manifest.Version, cancel);
            byte[] expected = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bindingBytes).Replace("\"revision\": 1", "\"revision\": 2", StringComparison.Ordinal));
            Assert.Equal(expected, File.ReadAllBytes(bindingFile));
        }
        else
        {
            var ex = await Assert.ThrowsAsync<PackFormatException>(() => workspaces.LockPackAsync("plant", manifest.Id, "2.0.0", cancel));
            Assert.StartsWith("/bindingVersion", ex.Message, StringComparison.Ordinal);
            Assert.Equal(bindingBytes, File.ReadAllBytes(bindingFile));
        }
    }
}
