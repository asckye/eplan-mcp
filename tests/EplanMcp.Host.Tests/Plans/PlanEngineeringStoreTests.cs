using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanEngineeringStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("cabinet", "\"a2\"")]
    [InlineData("configValuesAndTrace", "[]")]
    [InlineData("configDigest", "\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"")]
    [InlineData("workflowRefAndInputs", "{\"inputs\":{\"power\":8}}")]
    [InlineData("moduleBindings", "[{\"parameters\":{\"power\":8}}]")]
    [InlineData("ruleDigests", "{}")]
    [InlineData("acceptance", "{\"checks\":[{\"expected\":8}]}")]
    public async Task Schema2_StoredEngineeringTampering_RefusesWithoutRewritingFile(string field, string replacement)
    {
        using var f = new PlanStoreFixture();
        PlanDefinition definition = PlanEngineeringTests.Definition() with { Digests = f.Definition.Digests };
        PlanRecord published = await f.Store.PublishAsync(definition, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token);
        PlanRecord read = Assert.IsType<PlanRecord>(await f.Store.ReadAsync(definition.PlanId, Token));
        Assert.Equal(2, read.SchemaVersion);
        Assert.Equal("3", read.Definition.CompilerVersion);
        Assert.Equal(published.PlanHash, read.PlanHash);
        Assert.Equal(PlanCanonical.Serialize(definition), PlanCanonical.Serialize(read.Definition));
        await f.Store.UpdateAsync(definition.PlanId, 0, s => s.Admit("attempt", "task", "host"), Token);
        Assert.Equal(published.PlanHash, (await f.Store.ReadAsync(definition.PlanId, Token))!.PlanHash);

        JsonNode node = JsonNode.Parse(await File.ReadAllTextAsync(f.Record, Token))!;
        node["definition"]!["engineering"]![field] = JsonNode.Parse(replacement);
        string tampered = node.ToJsonString();
        await File.WriteAllTextAsync(f.Record, tampered, Token);

        JsonException error = await Assert.ThrowsAsync<JsonException>(() => f.Store.ReadAsync(definition.PlanId, Token));
        Assert.Contains("hash", error.Message, StringComparison.Ordinal);
        Assert.Equal(tampered, await File.ReadAllTextAsync(f.Record, Token));
    }

    [Theory]
    [InlineData(1, 2, "3")]
    [InlineData(2, 1, "2")]
    [InlineData(2, 2, "2")]
    [InlineData(1, 1, "9")]
    [InlineData(9, 2, "3")]
    public async Task Read_UnsupportedOrMismatchedVersions_PreservesFile(int envelope, int schema, string compiler)
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        PlanDefinition definition = schema == 1 ? f.Definition : PlanEngineeringTests.Definition();
        var record = new PlanRecord(envelope, "hash", definition with { SchemaVersion = schema, CompilerVersion = compiler }, new());
        string json = JsonSerializer.Serialize(record, PlanJson.Options);
        await File.WriteAllTextAsync(f.Record, json, Token);

        await Assert.ThrowsAnyAsync<Exception>(() => f.Store.ReadAsync(definition.PlanId, Token));
        Assert.Equal(json, await File.ReadAllTextAsync(f.Record, Token));
    }
}
