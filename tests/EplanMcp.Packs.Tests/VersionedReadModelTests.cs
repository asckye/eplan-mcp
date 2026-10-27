using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class VersionedReadModelTests
{
    internal const string Binding = """
        {"bindingVersion":2,"project":"plant","revision":7,"locks":[],"projectValues":{},
         "packRoots":[{"id":"acme.panels","version":"1.2.0"}],
         "cabinets":[{"id":"a1","title":"Power","layers":["acme.panels@1.2.0:config_layer/company"],
          "values":{"motor.power":{"default":7.5,"constraint":{"allowed":{"min":0.5,"max":10}}}},
          "structure":{"prefixes":[{"installation":"=AP","location":"+A1"}]}}]}
        """;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Manifest_KnownVersion_RoundTripsWithSlotsUnchanged(int version)
    {
        byte[] json = PackJson.Serialize(Manifests.Complete() with { SchemaVersion = version });
        PackManifest manifest = PackJson.ReadManifest(json);

        Assert.Empty(PackManifestValidator.Validate(manifest));
        Assert.Equal(json, PackJson.Serialize(manifest));
        Assert.Equal(PackDigest.Compute(json), PackDigest.Compute(PackJson.Serialize(manifest)));
    }

    [Theory]
    [InlineData("config_schema", ResourceKind.ConfigSchema)]
    [InlineData("config_layer", ResourceKind.ConfigLayer)]
    [InlineData("fact_set", ResourceKind.FactSet)]
    [InlineData("rule", ResourceKind.Rule)]
    [InlineData("workflow", ResourceKind.Workflow)]
    [InlineData("sample", ResourceKind.Sample)]
    [InlineData("module", ResourceKind.Module)]
    [InlineData("material_mapping", ResourceKind.MaterialMapping)]
    [InlineData("candidate", ResourceKind.Rule)]
    [InlineData("gap", ResourceKind.Gap)]
    public void Knowledge_V2KindsAndDecimalOpaqueSpecs_RoundTrip(string kind, ResourceKind resourceKind)
    {
        byte[] json = Utf8($$$"""{"apiVersion":"eplanmcp.pack/v2","kind":"{{{kind}}}","metadata":{"name":"motor"},"origin":{"asset":"document/guide","page":"=AP+A1/1"},"spec":{"futureBody":[7.5,true,{"value":0.125}]}}""");
        KnowledgeDocument read = KnowledgeDocument.Read(json);
        byte[] stored = PackJson.Serialize(read);

        Assert.Equal(7.5m, (decimal)read.Spec["futureBody"]![0]!);
        Assert.Equal(resourceKind, KnowledgeDocument.ResourceKindOf(read.Kind));
        Assert.Equal($"{kind}/motor", AssetNames.Local(Manifests.File(KnowledgeDocument.PathOf(read.Kind, "motor"), resourceKind)));
        Assert.Equal(stored, PackJson.Serialize(KnowledgeDocument.Read(stored)));
        Assert.DoesNotContain('\r', Encoding.UTF8.GetString(stored));
        Assert.Throws<PackFormatException>(() => PackDigest.Compute(json));
    }

    [Theory]
    [InlineData("candidate", "{\"statement\":\"x\",\"confidence\":\"high\",\"status\":\"proposed\"}")]
    [InlineData("module", "{\"macro\":\"macro/motor\",\"parameters\":[]}")]
    [InlineData("material_mapping", "{\"entries\":[]}")]
    [InlineData("gap", "{\"missing\":\"x\",\"status\":\"open\"}")]
    public void Knowledge_V1_BytesRemainUnchangedAfterReadAndRoundTrip(string kind, string spec)
    {
        KnowledgeDocument document = KnowledgeDocument.Read(Utf8($$"""{"apiVersion":"eplanmcp.pack/v1","kind":"{{kind}}","metadata":{"name":"motor"},"spec":{{spec}}}"""));
        byte[] original = PackJson.Serialize(document);

        Assert.Equal(original, PackJson.Serialize(KnowledgeDocument.Read(original)));
    }

    [Theory]
    [InlineData("\"apiVersion\":\"eplanmcp.pack/v2\"", "\"apiVersion\":\"eplanmcp.pack/v3\"", "/apiVersion")]
    [InlineData("\"kind\":\"module\"", "\"kind\":\"unknown\"", "kind")]
    [InlineData("\"name\":\"motor\"", "\"name\":\"motor\",\"status\":\"official\"", "status")]
    [InlineData("\"asset\":\"module/motor\"", "\"asset\":\"module/motor\",\"extra\":true", "extra")]
    [InlineData("\"spec\":{}", "\"spec\":{},\"id\":\"persisted\"", "id")]
    [InlineData("\"spec\":{}", "\"spec\":[]", "spec")]
    [InlineData("\"spec\":{}", "\"spec\":null", "spec")]
    [InlineData("\"spec\":{}", "\"spec\":{\"nested\":{\"x\":1,\"x\":2}}", "JSON")]
    [InlineData("\"spec\":{}", "\"spec\":{\"x\":1e1000}", "/spec/x")]
    [InlineData("\"kind\":\"module\"", "\"kind\":\"module\",\"kind\":\"rule\"", "JSON")]
    public void Knowledge_InvalidEnvelopeOrNumbers_IsRefused(string original, string replacement, string problem)
    {
        const string Json = """{"apiVersion":"eplanmcp.pack/v2","kind":"module","metadata":{"name":"motor"},"origin":{"asset":"module/motor"},"spec":{}}""";

        var ex = Assert.Throws<PackFormatException>(() => KnowledgeDocument.Read(Utf8(Json.Replace(original, replacement, StringComparison.Ordinal))));

        Assert.Contains(problem, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Knowledge_V1NewKind_IsRefused()
    {
        var ex = Assert.Throws<PackFormatException>(() => KnowledgeDocument.Read(Utf8("""{"apiVersion":"eplanmcp.pack/v1","kind":"rule","metadata":{"name":"x"},"spec":{}}""")));

        Assert.StartsWith("/kind:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_V2_PreservesAllReadFieldsAndDecimals()
    {
        ProjectBinding read = PackJson.ReadBinding(Utf8(Binding));
        byte[] stored = PackJson.Serialize(read);

        Assert.Equal(2, read.BindingVersion);
        Assert.Equal(("acme.panels", "1.2.0"), (read.PackRoots![0].Id, read.PackRoots[0].Version));
        Assert.Equal("+A1", (string?)read.Cabinets[0].Structure!["prefixes"]![0]!["location"]);
        Assert.Equal(7.5m, (decimal)read.Cabinets[0].Values!["motor.power"]!["default"]!);
        Assert.Equal(stored, PackJson.Serialize(PackJson.ReadBinding(stored)));
        Assert.Equal("+A10", (string?)PackJson.ReadBinding(Utf8(Binding.Replace("+A1", "+A10", StringComparison.Ordinal))).Cabinets[0].Structure!["prefixes"]![0]!["location"]);
    }

    [Theory]
    [InlineData("\"bindingVersion\":2", "\"bindingVersion\":3", "bindingVersion 3")]
    [InlineData("\"project\":\"plant\"", "\"project\":null", "/project")]
    [InlineData("\"revision\":7", "\"revision\":\"7\"", "/revision")]
    [InlineData("\"revision\":7", "\"revision\":-1", "/revision")]
    [InlineData("\"projectValues\":{}", "\"projectValues\":null", "/projectValues")]
    [InlineData("\"projectValues\":{}", "\"projectValues\":{\"a~/b\":{\"extra\":true}}", "/projectValues/a~0~1b/extra")]
    [InlineData("\"version\":\"1.2.0\"", "\"version\":\"^1.2.0\"", "/packRoots/0/version")]
    [InlineData("\"version\":\"1.2.0\"", "\"version\":\"1.2.0\",\"digest\":\"x\"", "/packRoots/0/digest")]
    [InlineData("\"layers\":[\"acme.panels@1.2.0:config_layer/company\"]", "\"layers\":[\"config_layer/company\"]", "/cabinets/0/layers/0")]
    [InlineData("\"id\":\"a1\"", "\"id\":\"a1\",\"extra\":1", "/cabinets/0/extra")]
    [InlineData("\"location\":\"+A1\"", "\"location\":\"+A*\"", "/cabinets/0/structure/prefixes/0/location")]
    [InlineData("\"location\":\"+A1\"", "\"location\":\"+A[0-9]\"", "/cabinets/0/structure/prefixes/0/location")]
    [InlineData("\"location\":\"+A1\"", "\"location\":\"+A1+B2\"", "/cabinets/0/structure/prefixes/0/location")]
    [InlineData("\"location\":\"+A1\"", "\"location\":\"A1\"", "/cabinets/0/structure/prefixes/0/location")]
    [InlineData("\"location\":\"+A1\"", "\"locations\":\"+A1\"", "/cabinets/0/structure/prefixes/0/locations")]
    [InlineData("\"prefixes\":[{\"installation\":\"=AP\",\"location\":\"+A1\"}]", "\"all\":false", "/cabinets/0/structure")]
    [InlineData("\"prefixes\":[{\"installation\":\"=AP\",\"location\":\"+A1\"}]", "\"all\":true", "/cabinets/0/structure")]
    [InlineData("\"prefixes\":[{\"installation\":\"=AP\",\"location\":\"+A1\"}]", "\"prefixes\":[]", "/cabinets/0/structure")]
    [InlineData("\"default\":7.5", "\"default\":{}", "/cabinets/0/values/motor.power/default")]
    [InlineData("\"constraint\":{\"allowed\":{\"min\":0.5,\"max\":10}}", "\"constraint\":{\"allowed\":{\"min\":0.5,\"max\":10,\"extra\":0}}", "/cabinets/0/values/motor.power/constraint/allowed/extra")]
    public void Binding_InvalidV2_ReportsJsonPointer(string original, string replacement, string expectedPointer)
    {
        Assert.Contains(original, Binding, StringComparison.Ordinal);
        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(Utf8(Binding.Replace(original, replacement, StringComparison.Ordinal))));

        Assert.Contains(expectedPointer, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_DefaultAll_IsAcceptedOnlyAsSoleCabinet()
    {
        JsonObject node = JsonNode.Parse(Binding)!.AsObject();
        node["cabinets"]![0]!["id"] = "default";
        node["cabinets"]![0]!["structure"] = new JsonObject { ["all"] = true };
        Assert.NotNull(PackJson.ReadBinding(PackJson.Serialize(node)));
        node["cabinets"]!.AsArray().Add(node["cabinets"]![0]!.DeepClone());

        Assert.Contains("/cabinets/0/structure", Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(PackJson.Serialize(node))).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("projectValues", "{}")]
    [InlineData("packRoots", "[]")]
    public void Binding_V1_RejectsV2Members(string name, string value)
    {
        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(Utf8($$"""{"bindingVersion":1,"project":"plant","revision":1,"{{name}}":{{value}}}""")));

        Assert.Contains("/" + name, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("layers", "[]")]
    [InlineData("values", "{}")]
    [InlineData("structure", "{}")]
    public void Binding_V1_RejectsV2CabinetMembers(string name, string value)
    {
        var ex = Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(Utf8($$"""{"bindingVersion":1,"project":"plant","revision":1,"cabinets":[{"id":"default","{{name}}":{{value}}}]}""")));

        Assert.Contains("/cabinets/0/" + name, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_V1_RoundTripRemainsByteIdentical()
    {
        byte[] original = PackJson.Serialize(new ProjectBinding { BindingVersion = 1, Project = "plant", Revision = 1, Cabinets = [new() { Id = "default" }] });

        Assert.Equal(original, PackJson.Serialize(PackJson.ReadBinding(original)));
        Assert.DoesNotContain("projectValues", Encoding.UTF8.GetString(original), StringComparison.Ordinal);
        Assert.DoesNotContain("layers", Encoding.UTF8.GetString(original), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Over16MiB_IsRefusedForKnowledgeAndBindings()
    {
        byte[] large = new byte[PackJson.MaxFileBytes + 1];

        Assert.Contains("16 MB", Assert.Throws<PackFormatException>(() => KnowledgeDocument.Read(large)).Message, StringComparison.Ordinal);
        Assert.Contains("16 MB", Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(large)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("locks")]
    [InlineData("packRoots")]
    [InlineData("cabinets")]
    public void Binding_V2DuplicateIds_AreRefused(string collection)
    {
        JsonObject node = JsonNode.Parse(Binding)!.AsObject();
        if (collection == "locks")
        {
            node[collection]!.AsArray().Add(new JsonObject { ["id"] = "acme.panels", ["version"] = "1.2.0", ["digest"] = Manifests.Hash('a') });
        }

        node[collection]!.AsArray().Add(node[collection]![0]!.DeepClone());

        Assert.StartsWith($"/{collection}/1/id:", Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(PackJson.Serialize(node))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_V1PreviouslyAcceptedValues_ReadUnchanged()
    {
        byte[] json = Utf8("""{"bindingVersion":1,"project":"","revision":-1,"cabinets":[{"id":"x","title":null},{"id":"x"}],"locks":[{"id":"x","version":"old","digest":"old"}]}""");

        ProjectBinding read = PackJson.ReadBinding(json);

        Assert.Equal(-1, read.Revision);
        Assert.Equal(2, read.Cabinets.Count);
        Assert.Null(read.Cabinets[0].Title);
        Assert.Equal("old", read.Locks[0].Version);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("module")]
    public void Knowledge_V1LargeNumbers_RemainReadable(string kind)
    {
        byte[] json = Utf8($$$"""{"apiVersion":"eplanmcp.pack/v1","kind":"{{{kind}}}","metadata":{"name":"x"},"spec":{"value":1e1000}}""");

        Assert.NotNull(KnowledgeDocument.Read(json));
    }

    [Fact]
    public void Knowledge_InvalidUtf8_IsRefused()
    {
        byte[] json = Utf8("""{"apiVersion":"eplanmcp.pack/v2","kind":"module","metadata":{"name":"x"},"spec":{"text":"X"}}""");
        json[Array.IndexOf(json, (byte)'X')] = 0xff;

        Assert.Throws<PackFormatException>(() => KnowledgeDocument.Read(json));
    }

    [Theory]
    [InlineData("\"apiVersion\":\"eplanmcp.pack/v2\"", "\"apiVersion\":\"eplanmcp.pack/v2\",\"apiVersion\":\"eplanmcp.pack/v2\"")]
    [InlineData("\"metadata\":{\"name\":\"x\"}", "\"metadata\":{\"name\":\"x\",\"name\":\"x\"}")]
    public void Knowledge_DuplicateEnvelopeMembers_AreRefused(string original, string replacement)
    {
        const string Json = """{"apiVersion":"eplanmcp.pack/v2","kind":"module","metadata":{"name":"x"},"spec":{}}""";

        Assert.Throws<PackFormatException>(() => KnowledgeDocument.Read(Utf8(Json.Replace(original, replacement, StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("\"revision\":7", "\"revision\":7,\"revision\":7")]
    [InlineData("\"default\":7.5", "\"default\":7.5,\"default\":7.5")]
    [InlineData("\"location\":\"+A1\"", "\"location\":\"+A1\",\"location\":\"+A1\"")]
    public void Binding_DuplicateMembersAtEveryDepth_AreRefused(string original, string replacement)
    {
        Assert.Throws<PackFormatException>(() => PackJson.ReadBinding(Utf8(Binding.Replace(original, replacement, StringComparison.Ordinal))));
    }

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);
}
