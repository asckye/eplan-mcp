using System.Text.Json;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class ConfigMergePackTests
{
    [Fact]
    public async Task Merge_TwoVerifiedPacksWithDifferentMotorSchemas_ReportsBothExactResourcesAndHashes()
    {
        using var first = new TempFolder();
        using var second = new TempFolder();
        ConfigDocument firstSchema = await WritePackAsync(first, "sample.motors", false);
        ConfigDocument secondSchema = await WritePackAsync(second, "other.motors", true);

        ConfigMergeResult result = ConfigMerger.Merge([firstSchema, secondSchema], [], ConfigMergerTests.Binding(), 0);

        Assert.Equal(ConfigMerger.ConfigConflict, result.Reason);
        Assert.Equal("/spec/items/motor.power", result.Location);
        Assert.Empty(result.Values.EnumerateArray());
        Assert.Equal(new[] { firstSchema.Resource, secondSchema.Resource }, result.Trace.EnumerateArray().Select(t => t.GetProperty("source").GetProperty("resource").GetString()));
        Assert.Equal(new[] { firstSchema.Sha256, secondSchema.Sha256 }, result.Trace.EnumerateArray().Select(t => t.GetProperty("source").GetProperty("sha256").GetString()));
        Assert.NotEqual(firstSchema.Sha256, secondSchema.Sha256);
    }

    private static async Task<ConfigDocument> WritePackAsync(TempFolder folder, string id, bool different)
    {
        KnowledgeDocument schema = ConfigFixtures.Document(ConfigFixtures.SchemaPath);
        if (different)
        {
            schema.Spec["items"]!["motor.power"]!["max"] = 200;
        }

        folder.Write(ConfigFixtures.SchemaPath, PackJson.Serialize(schema));
        PackResource resource = await PackFolders.ResourceAsync(folder, ConfigFixtures.SchemaPath, ResourceKind.ConfigSchema);
        PackManifest manifest = await PackFolders.WriteAsync(folder, m => m with { Id = id, SchemaVersion = 2, Resources = [.. m.Resources.Append(resource).OrderBy(r => r.Path, StringComparer.Ordinal)] });
        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);
        Assert.True(check.Ok, string.Join("; ", check.Problems));
        KnowledgeDocument read = KnowledgeDocument.Read(File.ReadAllBytes(folder.PathOf(ConfigFixtures.SchemaPath)));
        return new(AssetNames.Id(manifest.Id, manifest.Version, "config_schema/motors"), resource.Sha256, JsonSerializer.SerializeToElement(read.Spec, PackJson.Options));
    }
}
