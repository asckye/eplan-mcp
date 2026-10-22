using System.Text;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackJsonTests
{
    [Fact]
    public void Manifest_RoundTrip_GivesTheSameBytes()
    {
        byte[] first = PackJson.Serialize(Manifests.Complete());

        byte[] second = PackJson.Serialize(PackJson.ReadManifest(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Serialize_WritesSnakeCaseValuesLfAndNoNulls()
    {
        string json = Encoding.UTF8.GetString(PackJson.Serialize(Manifests.Minimal(Manifests.File("a.zw1", ResourceKind.ProjectBackup))));

        Assert.Contains("\"kind\": \"project_backup\"", json, StringComparison.Ordinal);
        Assert.Contains("\"storage\": \"bundled\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"schemaVersion\": 1,", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, "pack.json has schemaVersion 2; this EplanMcp reads schemaVersion 1 only.")]
    [InlineData(0, "pack.json has schemaVersion 0; this EplanMcp reads schemaVersion 1 only.")]
    public void ReadManifest_UnknownSchemaVersion_IsRefusedByItsVersion(int version, string message)
    {
        // A future version may add properties; the reader names the version, not the first unknown property.
        byte[] json = Encoding.UTF8.GetBytes($$"""{ "schemaVersion": {{version}}, "futureProperty": true }""");

        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadManifest(json));

        Assert.StartsWith(message, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "id": "x" }""")]
    [InlineData("""{ "schemaVersion": "1" }""")]
    [InlineData("""[1]""")]
    public void ReadManifest_WithoutSchemaVersion_IsRefused(string json)
    {
        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadManifest(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("pack.json has no schemaVersion; it is not a file of an EplanMcp pack.", ex.Message);
    }

    [Theory]
    [InlineData("\"name\": \"ACME cabinet base\",", "\"name\": \"ACME cabinet base\", \"extra\": 1,")]
    [InlineData("\"name\": \"ACME cabinet base\",", "")]
    [InlineData("\"name\": \"ACME cabinet base\",", "\"name\": null,")]
    [InlineData("\"kind\": \"project_backup\"", "\"kind\": 1")]
    [InlineData("\"bytes\": 10,", "\"bytes\": \"10\",")]
    [InlineData("\"bytes\": 10,", "\"Bytes\": 10,")]
    public void ReadManifest_OffTheShape_IsRefused(string original, string replacement)
    {
        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadManifest(Replaced(original, replacement)));

        Assert.StartsWith("pack.json is not valid for schemaVersion 1: ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadManifest_DuplicateProperty_IsRefused()
    {
        byte[] json = Replaced("\"name\": \"ACME cabinet base\",", "\"name\": \"ACME cabinet base\", \"name\": \"Other\",");

        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadManifest(json));

        Assert.StartsWith("pack.json is not valid JSON: ", ex.Message, StringComparison.Ordinal);
    }

    private static byte[] Replaced(string original, string replacement)
    {
        string json = Encoding.UTF8.GetString(PackJson.Serialize(Manifests.Minimal(Manifests.File("a.zw1", ResourceKind.ProjectBackup))));
        Assert.Contains(original, json, StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json.Replace(original, replacement, StringComparison.Ordinal));
    }

    [Fact]
    public void ReadManifest_WithUtf8Bom_IsRead()
    {
        byte[] json = [0xEF, 0xBB, 0xBF, .. PackJson.Serialize(Manifests.Complete())];

        Assert.Equal("acme.cabinet-base", PackJson.ReadManifest(json).Id);
    }

    [Fact]
    public void ReadManifest_NotJson_IsRefused()
    {
        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadManifest(Encoding.UTF8.GetBytes("{ schemaVersion: 1")));

        Assert.StartsWith("pack.json is not valid JSON: ", ex.Message, StringComparison.Ordinal);
    }
}
