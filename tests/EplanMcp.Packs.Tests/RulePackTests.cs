using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class RulePackTests
{
    private const string RulePath = "knowledge/rule/starter-allowed.json";

    [Fact]
    public async Task Check_ValidAndInvalidRuleFixturesWithCorrectHashes_ValidatesAst()
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder, ConfigFixtures.Read("Rules/valid.json"));
        PackCheckResult valid = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);
        Assert.True(valid.Ok, string.Join("; ", valid.Problems));

        folder.Write(RulePath, ConfigFixtures.Read("Rules/invalid.json"));
        PackResource updated = await PackFolders.ResourceAsync(folder, RulePath, ResourceKind.Rule);
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with { Resources = [.. manifest.Resources.Select(r => r.Path == RulePath ? updated : r)] }));

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);
        Assert.Equal(RulePath, problem.Location);
        Assert.StartsWith("/spec/check/some/relation:", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("kind", "\"module\"", "/kind")]
    [InlineData("metadata", "{\"name\":\"other\"}", "/metadata/name")]
    [InlineData("apiVersion", "\"eplanmcp.pack/v3\"", "/apiVersion")]
    public async Task Check_RuleEnvelopeDoesNotMatchResource_IsRefused(string key, string json, string expectedPointer)
    {
        using var folder = new TempFolder();
        JsonObject document = JsonNode.Parse(ConfigFixtures.Read("Rules/valid.json"))!.AsObject();
        document[key] = JsonNode.Parse(json);
        await WriteAsync(folder, PackJson.Serialize(document));

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);

        Assert.Equal(RulePath, problem.Location);
        Assert.StartsWith(expectedPointer + ":", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_RulePathMislabeledAsModule_CannotBypassValidation()
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder, ConfigFixtures.Read("Rules/invalid.json"));
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r.Path == RulePath ? r with { Kind = ResourceKind.Module } : r)],
        }));

        Assert.StartsWith("/kind:", Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_ReferencedRuleKnowledge_IsRefused()
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder, ConfigFixtures.Read("Rules/valid.json"));
        File.Delete(folder.PathOf(RulePath));
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r.Path == RulePath ? r with
            {
                Storage = ResourceStorage.Referenced,
                Reference = new ResourceReference { Root = "samples", Path = "starter.json" },
            } : r)],
        }));

        Assert.StartsWith("/spec:", Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_UnknownConfigOperand_IsRefused()
    {
        using var folder = new TempFolder();
        KnowledgeDocument document = KnowledgeDocument.Read(ConfigFixtures.Read("Rules/valid.json"));
        document.Spec["check"] = JsonNode.Parse("""{"exists":[{"config":"motor.missing"}]}""");
        await WriteAsync(folder, PackJson.Serialize(document));

        Assert.StartsWith("/spec/check/exists/0/config:", Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_DuplicateAstMembers_AreRefused()
    {
        using var folder = new TempFolder();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(ConfigFixtures.Read("Rules/valid.json"))
            .Replace("\"property\": \"function.category\"", "\"property\": \"function.category\", \"property\": \"function.name\"", StringComparison.Ordinal));
        await WriteAsync(folder, bytes);

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);

        Assert.Equal(RulePath, problem.Location);
        Assert.Contains("JSON", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"spec":{"source":"legacy note","check":"legacy text"}}""")]
    [InlineData("""{"kind":"rule","spec":{"source":"legacy note","check":"legacy text"}}""")]
    [InlineData("""{"apiVersion":"eplanmcp.pack/v1","kind":"rule","spec":{"source":"legacy note","check":"legacy text"}}""")]
    public async Task Check_LegacyBareRuleAndCandidate_RemainReadable(string bareRule)
    {
        using var folder = new TempFolder();
        const string Bare = "resources/rules/legacy.json";
        const string Candidate = "knowledge/candidate/motor.json";
        folder.Write(Bare, bareRule);
        folder.Write(Candidate, """{"apiVersion":"eplanmcp.pack/v1","kind":"candidate","metadata":{"name":"motor"},"spec":{"statement":"Needs review"}}""");
        var resources = new List<PackResource> { await PackFolders.ResourceAsync(folder, Bare, ResourceKind.Rule), await PackFolders.ResourceAsync(folder, Candidate, ResourceKind.Rule) };
        folder.Write(PackManifest.FileName, PackJson.Serialize(Manifests.Minimal([.. resources])));

        Assert.True((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Ok);
    }

    private static async Task<PackManifest> WriteAsync(TempFolder folder, byte[] bytes)
    {
        folder.Write(RulePath, bytes);
        folder.Write(ConfigFixtures.SchemaPath, ConfigFixtures.Read("MotorsPack/" + ConfigFixtures.SchemaPath));
        PackManifest manifest = Manifests.Minimal(await PackFolders.ResourceAsync(folder, RulePath, ResourceKind.Rule),
            await PackFolders.ResourceAsync(folder, ConfigFixtures.SchemaPath, ResourceKind.ConfigSchema)) with
        { SchemaVersion = 2 };
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest));
        return manifest;
    }
}
