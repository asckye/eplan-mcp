using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Tests.Shared;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class ModuleInspectionTests
{
    [Fact]
    public void Read_CompleteEvidence_PreservesCrossPageClosureAndAllLanguages()
    {
        ModuleInspection report = Read(ModuleInspectionEvidence.Create());
        Assert.True(report.Complete);
        Assert.Equal(6, report.Evidence.GetProperty("objects").GetArrayLength());
        Assert.Equal(3, report.Evidence.GetProperty("references").GetArrayLength());
        JsonElement values = report.Evidence.GetProperty("placeholders")[0].GetProperty("valueSets")[0].GetProperty("values").GetProperty("text");
        Assert.Equal(["en_US", "de_DE"], values.EnumerateArray().Select(v => v.GetProperty("language").GetString()));
        Assert.Null(report.Declarations);
    }

    [Theory]
    [InlineData("/complete", "false", "/complete")]
    [InlineData("/apiExtensionLicence", "null", "/apiExtensionLicence")]
    [InlineData("/coverage/crossPageReferences", "false", "/coverage/crossPageReferences")]
    [InlineData("/coverage/properties", "false", "/coverage/properties")]
    [InlineData("/references/0/to", "\"unresolved/42\"", "/references/0")]
    [InlineData("/objects/3/page", "\"=AP+A2/3\"", "/objects")]
    [InlineData("/objects/2/properties/0", "{\"propertyId\":20011,\"index\":0,\"language\":\"en_US\",\"state\":\"unavailable\"}", "/objects/2/properties/0")]
    [InlineData("/placeholders/0/bindings", "[]", "/placeholders/0/bindings")]
    [InlineData("/placeholders", "[]", "/placeholders")]
    [InlineData("/placeholders/0/bindings/0/target", "\"unresolved/43\"", "/placeholders/0/bindings/0")]
    [InlineData("/placeholders/0/bindings/0/index", "1", "/placeholders/0/bindings/0")]
    [InlineData("/placeholders/0/valueSets/0/values", "{}", "/placeholders/0/valueSets/0/values/text")]
    [InlineData("/placeholders/0/valueSets/0/values/text", "[]", "/placeholders/0/valueSets/0/values")]
    [InlineData("/gaps", "[{\"pointer\":\"/objects\",\"message\":\"A graphic binding could not be resolved.\"}]", "/objects")]
    public void Read_IncompleteDiscovery_NeverReportsComplete(string path, string json, string gap)
    {
        JsonObject evidence = ModuleInspectionEvidence.Create();
        ModuleFixtures.Set(evidence, path, json);
        ModuleInspection result = Read(evidence);
        Assert.False(result.Complete);
        Assert.Contains(result.Gaps, g => g.Subject == gap);
    }

    [Theory]
    [InlineData("/unknown", "true", "/unknown")]
    [InlineData("/coverage/extra", "true", "/coverage/extra")]
    [InlineData("/objects/0/extra", "true", "/objects/0/extra")]
    [InlineData("/objects/1/id", "\"page/1\"", "/objects/1/id")]
    [InlineData("/objects/0/id", "\"C:/private\"", "/objects/0/id")]
    [InlineData("/objects/0/kind", "\"invented\"", "/objects/0/kind")]
    [InlineData("/references/0/kind", "\"invented\"", "/references/0/kind")]
    [InlineData("/placeholders/0/bindings/0/propertyId", "-1", "/placeholders/0/bindings/0")]
    [InlineData("/placeholders/0/valueSets/0/values/text/0/value", "1e1000", "/placeholders/0/valueSets/0/values/text/0/value")]
    [InlineData("/macroHash", "\"mismatch\"", "/macroHash")]
    [InlineData("/objects", "null", "/objects")]
    public void Read_InvalidMembersTypesNumbersOrHash_RejectsWithPointer(string path, string json, string expectedPointer)
    {
        JsonObject evidence = ModuleInspectionEvidence.Create();
        ModuleFixtures.Set(evidence, path, json);
        Assert.Contains(expectedPointer, Assert.Throws<PackFormatException>(() => Read(evidence)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_DuplicateMembers_Rejects()
    {
        string json = ModuleInspectionEvidence.Create().ToJsonString().Replace("\"complete\":true", "\"complete\":true,\"complete\":true", StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Throws<PackFormatException>(() => ModuleInspectionReader.Read(document.RootElement, ModuleInspectionEvidence.Hash));
    }

    [Fact]
    public void Read_ValidatedModule_ReturnsDeclarationsAndUnprovenBindingsAsGaps()
    {
        ModuleDefinition module = ModuleFixtures.Module(ModuleFixtures.Body("motor-starter"));
        ModuleInspection report = ModuleInspectionReader.Read(ModuleInspectionEvidence.Element(ModuleInspectionEvidence.Create()), ModuleInspectionEvidence.Hash, module);
        Assert.False(report.Complete);
        Assert.True(JsonElement.DeepEquals(module.Spec, report.Declarations!.Value));
        Assert.Contains(report.Gaps, g => g.Subject == "/spec/parameters/power/binding");
        Assert.Contains(report.Gaps, g => g.Subject == "/spec/parameters/starter/binding");
    }

    [Fact]
    public void Read_BoundedClosure_RejectsOverflowInsteadOfTruncating()
    {
        JsonObject evidence = ModuleInspectionEvidence.Create();
        var edges = new JsonArray();
        for (int i = 0; i < 10001; i++)
        { edges.Add(evidence["references"]![0]!.DeepClone()); }
        evidence["references"] = edges;
        Assert.Contains("/references", Assert.Throws<PackFormatException>(() => Read(evidence)).Message, StringComparison.Ordinal);
    }

    private static ModuleInspection Read(JsonObject evidence) => ModuleInspectionReader.Read(ModuleInspectionEvidence.Element(evidence), ModuleInspectionEvidence.Hash);
}
