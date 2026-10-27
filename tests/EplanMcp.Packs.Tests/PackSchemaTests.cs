using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackSchemaTests
{
    [Fact]
    public void Manifest_DeclaresVersionPatternsAndClosedObjects()
    {
        JsonObject schema = PackSchema.Manifest();
        JsonObject properties = schema["properties"]!.AsObject();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)schema["$schema"]);
        Assert.Equal([1, 2], properties["schemaVersion"]!["enum"]!.AsArray().Select(n => (int)n!));
        Assert.Equal(PackPatterns.Id, (string?)properties["id"]!["pattern"]);
        Assert.Equal(PackPatterns.SemVer, (string?)properties["version"]!["pattern"]);
        Assert.False((bool?)schema["additionalProperties"]);
        Assert.Contains("resources", schema["required"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public void Manifest_ResourceKindsAreSnakeCase()
    {
        JsonObject resource = PackSchema.Manifest()["properties"]!["resources"]!["items"]!.AsObject();

        JsonArray kinds = resource["properties"]!["kind"]!["enum"]!.AsArray();

        Assert.Contains("basic_project", kinds.Select(n => (string?)n));
        Assert.Contains("material_mapping", kinds.Select(n => (string?)n));
        Assert.Equal(PackPatterns.Sha256, (string?)resource["properties"]!["sha256"]!["pattern"]);
        Assert.Equal(PackPatterns.Capability, (string?)PackSchema.Manifest()["properties"]!["compatibility"]!["properties"]!["capabilities"]!["items"]!["pattern"]);
    }
}
