using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class ConfigPackTests
{
    [Fact]
    public async Task Check_MotorFixtureWithCompanyAndCabinetType_IsValid()
    {
        using var folder = new TempFolder();
        PackManifest manifest = WriteFixture(folder);

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.Equal(manifest.Id, result.Manifest?.Id);
        Assert.Equal(3, result.Manifest!.Resources.Count);
    }

    [Theory]
    [InlineData("unsupported-unit", ConfigFixtures.SchemaPath, "/spec/items/motor.power/unit")]
    [InlineData("choice-outside-enum", ConfigFixtures.CompanyPath, "/spec/settings/motor.starter/default")]
    [InlineData("unknown-layer", ConfigFixtures.CompanyPath, "/spec/layer")]
    [InlineData("missing-source", ConfigFixtures.CompanyPath, "/spec/schema")]
    [InlineData("unknown-key", ConfigFixtures.CompanyPath, "/spec/settings/motor.unknown")]
    public async Task Check_InvalidConfigWithCorrectHashes_FailsWithFileAndPointer(string name, string path, string expectedPath)
    {
        using var folder = new TempFolder();
        PackManifest manifest = WriteFixture(folder);
        folder.Write(path, ConfigFixtures.Read($"InvalidConfig/{name}.json"));
        await RehashAsync(folder, manifest, path);

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains(result.Problems, p => p.Location == path && p.Message.StartsWith(expectedPath + ":", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.Message.Contains("does not match pack.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_SourceDocumentIsNotASchema_IsRejected()
    {
        using var folder = new TempFolder();
        PackManifest manifest = WriteFixture(folder);
        KnowledgeDocument document = ConfigFixtures.Document(ConfigFixtures.SchemaPath) with { Kind = KnowledgeKind.Module };
        folder.Write(ConfigFixtures.SchemaPath, PackJson.Serialize(document));
        await RehashAsync(folder, manifest, ConfigFixtures.SchemaPath);

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.Contains(result.Problems, p => p.Location == ConfigFixtures.SchemaPath && p.Message.StartsWith("/kind:", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.Location == ConfigFixtures.CompanyPath && p.Message.StartsWith("/spec/schema:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_SourceIsNotListedAsASchema_IsRejected()
    {
        using var folder = new TempFolder();
        PackManifest manifest = WriteFixture(folder);
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r.Path == ConfigFixtures.SchemaPath ? r with { Kind = ResourceKind.Module } : r)],
        }));

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.Contains(result.Problems, p => p.Location == ConfigFixtures.CompanyPath && p.Message.StartsWith("/spec/schema:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_ConfigPathListedAsAnotherKind_CannotBypassValidation()
    {
        using var folder = new TempFolder();
        PackManifest manifest = WriteFixture(folder);
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r with { Kind = ResourceKind.Module })],
        }));

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(3, result.Problems.Count);
        Assert.All(result.Problems, p => Assert.StartsWith("/kind:", p.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_MetadataDoesNotMatchResourceId_IsRejected()
    {
        using var folder = new TempFolder();
        PackManifest manifest = WriteFixture(folder);
        KnowledgeDocument document = ConfigFixtures.Document(ConfigFixtures.CompanyPath);
        folder.Write(ConfigFixtures.CompanyPath, PackJson.Serialize(document with { Metadata = document.Metadata with { Name = "other" } }));
        await RehashAsync(folder, manifest, ConfigFixtures.CompanyPath);

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);

        Assert.Equal(ConfigFixtures.CompanyPath, problem.Location);
        Assert.StartsWith("/metadata/name:", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Check_UnbundledConfigCannotBeValidated_IsRejected(bool mislabeled)
    {
        using var folder = new TempFolder();
        PackManifest manifest = WriteFixture(folder);
        File.Delete(folder.PathOf(ConfigFixtures.CompanyPath));
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r.Path == ConfigFixtures.CompanyPath ? r with
            {
                Storage = ResourceStorage.Referenced,
                Kind = mislabeled ? ResourceKind.Module : r.Kind,
                Reference = new ResourceReference { Root = "samples", Path = "company.json" },
            } : r)],
        }));

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);

        Assert.Equal(ConfigFixtures.CompanyPath, problem.Location);
        Assert.StartsWith("/spec:", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_InvalidConfig_IsNotPublished()
    {
        using var dev = new TempFolder();
        using var installed = new TempFolder();
        string id = PackJson.ReadManifest(ConfigFixtures.Read("MotorsPack/pack.json")).Id;
        var store = new DevPackStore(dev.Root);
        using var source = new TempFolder();
        PackManifest manifest = WriteFixture(source);
        source.Write(ConfigFixtures.CompanyPath, ConfigFixtures.Read("InvalidConfig/unknown-key.json"));
        await RehashAsync(source, manifest, ConfigFixtures.CompanyPath);
        Directory.Move(source.Root, store.FolderOf(id));
        Directory.CreateDirectory(source.Root);

        DevPackException error = await Assert.ThrowsAsync<DevPackException>(() => store.BuildAsync(id, "1.0.0", new PackStore(installed.Root),
            new Dictionary<string, string>(), "fixture", Manifests.CreatedAt, TestContext.Current.CancellationToken));

        Assert.Equal(DevPackError.Invalid, error.Error);
        Assert.False(Directory.Exists(Path.Combine(installed.Root, id)));
    }

    private static PackManifest WriteFixture(TempFolder folder)
    {
        byte[] bytes = ConfigFixtures.Read("MotorsPack/pack.json");
        PackManifest manifest = PackJson.ReadManifest(bytes);
        folder.Write(PackManifest.FileName, bytes);
        foreach (PackResource resource in manifest.Resources)
        {
            folder.Write(resource.Path, ConfigFixtures.Read("MotorsPack/" + resource.Path));
        }

        return manifest;
    }

    private static async Task RehashAsync(TempFolder folder, PackManifest manifest, string path)
    {
        PackResource original = manifest.Resources.Single(r => r.Path == path);
        PackResource updated = await PackFolders.ResourceAsync(folder, path, original.Kind);
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r.Path == path ? updated : r)],
            Provenance = manifest.Provenance with { Inputs = [new PackInput { Name = "inputs", Kind = PackInputKind.Folder }] },
        }));
    }
}
