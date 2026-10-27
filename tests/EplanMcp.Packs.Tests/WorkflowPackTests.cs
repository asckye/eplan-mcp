using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class WorkflowPackTests
{
    private const string WorkflowPath = "knowledge/workflow/motor.json";
    private const string MacroPath = "resources/macros/motor.ema";

    [Fact]
    public async Task Check_BundledMacroAndConfig_ValidatesWorkflowClosure()
    {
        using var folder = new TempFolder();
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"]![0]!["args"]!["pages"]![0]!["description"] = JsonNode.Parse("""{"config":"motor.starter"}""");
        folder.Write(ConfigFixtures.SchemaPath, ConfigFixtures.Read("MotorsPack/" + ConfigFixtures.SchemaPath));
        await WriteAsync(folder, spec, true, await PackFolders.ResourceAsync(folder, ConfigFixtures.SchemaPath, ResourceKind.ConfigSchema));

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.True(result.Ok, string.Join("; ", result.Problems));
    }

    [Theory]
    [InlineData("macro/missing.ema")]
    [InlineData("C:/external/motor.ema")]
    [InlineData("../motor.ema")]
    [InlineData("macro/motor.emp")]
    [InlineData("other@1.0.0:macro/motor.ema")]
    public async Task Check_ResourceOutsidePackClosure_IsRejected(string reference)
    {
        using var folder = new TempFolder();
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"]![1]!["args"]!["macro"] = reference;
        await WriteAsync(folder, spec);

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);

        Assert.Equal(WorkflowPath, problem.Location);
        Assert.StartsWith("/spec/steps/1/args/macro:", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Check_LockedDependency_RequiresExactVersionDigestAndResource(bool validDigest)
    {
        using var dependencyFolder = new TempFolder();
        dependencyFolder.Write(MacroPath, "fixture macro bytes");
        PackResource macro = await PackFolders.ResourceAsync(dependencyFolder, MacroPath, ResourceKind.Macro);
        PackManifest dependency = Manifests.Minimal(macro) with { Id = "acme.macros" };
        byte[] bytes = PackJson.Serialize(dependency);
        using var folder = new TempFolder();
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"]![1]!["args"]!["macro"] = "acme.macros@1.0.0:macro/motor.ema";
        PackManifest manifest = await WriteAsync(folder, spec, false);
        manifest = manifest with { Dependencies = [new PackDependency { Id = dependency.Id, Version = dependency.Version }] };
        byte[] manifestBytes = PackJson.Serialize(manifest);
        folder.Write(PackManifest.FileName, manifestBytes);
        folder.Write(PackManifest.LockFileName, PackJson.Serialize(PackLock.Create(new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(manifestBytes) },
            [new PackReference { Id = dependency.Id, Version = dependency.Version, Digest = validDigest ? PackDigest.Compute(bytes) : Manifests.Hash('f') }])));

        PackCheckResult result = await PackVerifier.CheckDirectoryWithDependenciesAsync(folder.Root, [bytes], TestContext.Current.CancellationToken);

        Assert.Equal(validDigest, result.Ok);
        if (!validDigest)
        {
            Assert.StartsWith("/spec/steps/1/args/macro:", Assert.Single(result.Problems).Message, StringComparison.Ordinal);
        }
        Assert.False((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Ok);
    }

    [Fact]
    public async Task Check_MacroOnlyInInactiveBranch_StillRequiresResourceClosure()
    {
        using var folder = new TempFolder();
        JsonObject spec = WorkflowFixtures.BranchSpec();
        spec["steps"]![2]!["args"]!["macro"] = "macro/missing.ema";
        await WriteAsync(folder, spec);

        Assert.StartsWith("/spec/steps/2/args/macro:", Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apiVersion", "\"eplanmcp.pack/v1\"", "/kind")]
    [InlineData("kind", "\"module\"", "/kind")]
    [InlineData("metadata", "{\"name\":\"other\"}", "/metadata/name")]
    public async Task Check_WorkflowEnvelopeMismatch_IsRejected(string member, string value, string expectedPointer)
    {
        using var folder = new TempFolder();
        await WriteAsync(folder, WorkflowFixtures.Spec());
        JsonObject document = JsonNode.Parse(await File.ReadAllBytesAsync(folder.PathOf(WorkflowPath), TestContext.Current.CancellationToken))!.AsObject();
        document[member] = JsonNode.Parse(value);
        folder.Write(WorkflowPath, PackJson.Serialize(document));
        PackManifest manifest = PackJson.ReadManifest(await File.ReadAllBytesAsync(folder.PathOf(PackManifest.FileName), TestContext.Current.CancellationToken));
        PackResource workflow = await PackFolders.ResourceAsync(folder, WorkflowPath, ResourceKind.Workflow);
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with { Resources = [.. manifest.Resources.Select(r => r.Path == WorkflowPath ? workflow : r)] }));

        Assert.StartsWith(expectedPointer + ":", Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Check_MislabeledOrReferencedWorkflow_CannotBypassValidation(bool referenced)
    {
        using var folder = new TempFolder();
        JsonObject spec = WorkflowFixtures.Spec();
        spec["include"] = "workflow/self";
        PackManifest manifest = await WriteAsync(folder, spec);
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r.Path == WorkflowPath ? r with
            {
                Kind = ResourceKind.Module,
                Storage = referenced ? ResourceStorage.Referenced : ResourceStorage.Bundled,
                Reference = referenced ? new ResourceReference { Root = "fixtures", Path = "motor.json" } : null,
            } : r)],
        }));
        if (referenced)
        {
            File.Delete(folder.PathOf(WorkflowPath));
        }

        Assert.False((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Ok);
    }

    [Fact]
    public async Task Check_MissingAcceptanceResource_IsRejected()
    {
        using var folder = new TempFolder();
        JsonObject spec = WorkflowFixtures.Spec();
        spec["acceptance"] = new JsonArray("rule/missing");
        await WriteAsync(folder, spec);

        Assert.StartsWith("/spec/acceptance:", Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems).Message, StringComparison.Ordinal);
    }

    private static async Task<PackManifest> WriteAsync(TempFolder folder, JsonObject spec, bool includeMacro = true, params PackResource[] additional)
    {
        folder.Write(WorkflowPath, PackJson.Serialize(new KnowledgeDocument { ApiVersion = KnowledgeDocument.V2ApiVersion, Kind = KnowledgeKind.Workflow, Metadata = new KnowledgeMetadata { Name = "motor" }, Spec = spec }));
        var resources = new List<PackResource>(additional) { await PackFolders.ResourceAsync(folder, WorkflowPath, ResourceKind.Workflow) };
        if (includeMacro)
        {
            folder.Write(MacroPath, "fixture macro bytes");
            resources.Add(await PackFolders.ResourceAsync(folder, MacroPath, ResourceKind.Macro));
        }
        PackManifest manifest = Manifests.Minimal([.. resources]) with { SchemaVersion = 2 };
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest));
        return manifest;
    }
}
