using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

/// <summary>The specs agents may write (docs/tools/eplan_asset_update.md, section 2).</summary>
[Trait("Size", "Small")]
public sealed class KnowledgeSpecsTests
{
    /// <summary>Resolves the ids these tests use: a local macro, a fact, an asset of another pack.</summary>
    private static (string, string)? Resolve(string reference) => reference switch
    {
        "acme.panels@dev:macro/ref/Motor.ema" => ("macro/ref/Motor.ema", "macro"),
        "fact:resources/macros/ref/Motor.ema#eplan_version" => (reference, "fact"),
        "acme.base@1.0.0:document/guide.md" => (reference, "document"),
        _ => null,
    };

    [Fact]
    public void Candidate_IsStoredProposedWithLocalReferences()
    {
        var spec = (JsonObject)JsonNode.Parse("""{"statement": "Every motor circuit\nhas an overload relay.", "basis": ["acme.panels@dev:macro/ref/Motor.ema", "fact:resources/macros/ref/Motor.ema#eplan_version"], "confidence": "medium"}""")!;

        (JsonObject stored, List<(string Pointer, string Message)> problems) = KnowledgeSpecs.Check(KnowledgeKind.Candidate, spec, Resolve);

        Assert.Empty(problems);
        Assert.Equal("""{"statement":"Every motor circuit\nhas an overload relay.","basis":["macro/ref/Motor.ema","fact:resources/macros/ref/Motor.ema#eplan_version"],"confidence":"medium","status":"proposed","inferredBy":"agent"}""", stored.ToJsonString());
    }

    [Theory]
    [InlineData(KnowledgeKind.Candidate, """{"statement": "x", "basis": [], "confidence": "medium"}""", "/spec/basis")]
    [InlineData(KnowledgeKind.Candidate, """{"statement": "x", "basis": ["nothing"], "confidence": "medium"}""", "/spec/basis/0")]
    [InlineData(KnowledgeKind.Candidate, """{"statement": "x", "basis": ["acme.base@1.0.0:document/guide.md"], "confidence": "sure"}""", "/spec/confidence")]
    [InlineData(KnowledgeKind.Candidate, """{"statement": "x", "basis": ["acme.base@1.0.0:document/guide.md"], "confidence": "low", "status": "accepted"}""", "/spec/status")]
    [InlineData(KnowledgeKind.Gap, """{"missing": "x", "status": "closed"}""", "/spec/status")]
    [InlineData(KnowledgeKind.Gap, """{"missing": "bell\u0007"}""", "/spec/missing")]
    [InlineData(KnowledgeKind.Gap, """{"related": []}""", "/spec/missing")]
    [InlineData(KnowledgeKind.Module, """{"macro": "acme.base@1.0.0:document/guide.md"}""", "/spec/macro")]
    [InlineData(KnowledgeKind.Module, """{"macro": "acme.panels@dev:macro/ref/Motor.ema", "parameters": [{"name": "power", "type": "float"}]}""", "/spec/parameters/0/type")]
    [InlineData(KnowledgeKind.Module, """{"macro": "acme.panels@dev:macro/ref/Motor.ema", "parameters": [{"name": "power", "type": "number", "range": 3}]}""", "/spec/parameters/0/range")]
    [InlineData(KnowledgeKind.MaterialMapping, """{"entries": [{"when": {}, "part": "SIE.3RT2015"}]}""", "/spec/entries/0/when")]
    [InlineData(KnowledgeKind.MaterialMapping, """{"entries": [], "rule": "x"}""", "/spec/rule")]
    public void BadSpecs_NameTheirPointer(KnowledgeKind kind, string json, string location)
    {
        (_, List<(string Pointer, string Message)> problems) = KnowledgeSpecs.Check(kind, (JsonObject)JsonNode.Parse(json)!, Resolve);

        Assert.Contains(problems, p => p.Pointer == location);
    }

    [Fact]
    public void GoodModuleAndMapping_PassAndGapsAreOpen()
    {
        (_, var module) = KnowledgeSpecs.Check(KnowledgeKind.Module, (JsonObject)JsonNode.Parse("""{"macro": "acme.panels@dev:macro/ref/Motor.ema", "description": "DOL motor", "parameters": [{"name": "power_kw", "type": "choice", "unit": "kW", "values": ["5.5", "7.5"], "placeholder": "P"}]}""")!, Resolve);
        (_, var mapping) = KnowledgeSpecs.Check(KnowledgeKind.MaterialMapping, (JsonObject)JsonNode.Parse("""{"entries": [{"when": {"power_kw": "5.5"}, "part": "SIE.3RT2015"}], "source": "acme.base@1.0.0:document/guide.md"}""")!, Resolve);
        (JsonObject gap, var gapProblems) = KnowledgeSpecs.Check(KnowledgeKind.Gap, (JsonObject)JsonNode.Parse("""{"missing": "No terminal rules."}""")!, Resolve);

        Assert.Empty(module.Concat(mapping).Concat(gapProblems));
        Assert.Equal("open", (string?)gap["status"]);
    }
}
