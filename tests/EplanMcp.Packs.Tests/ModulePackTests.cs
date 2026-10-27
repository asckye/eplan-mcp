using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Medium")]
public sealed class ModulePackTests
{
    [Fact]
    public async Task Check_MotorModuleTwoMappingsAndSample_PassWithRealFileHashes()
    {
        using var folder = new TempFolder();
        await WriteAsync(folder);

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.True(result.Ok, string.Join("; ", result.Problems));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("digest")]
    [InlineData("version")]
    [InlineData("manifest")]
    [InlineData("lock")]
    [InlineData("hash")]
    [InlineData("referenced")]
    [InlineData("page_macro")]
    public async Task Check_ModuleMacroInDependency_RequiresExactLockAndHash(string scenario)
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder);
        PackResource macro = Manifests.File(scenario == "page_macro" ? "resources/macros/Dependency.emp" : "resources/macros/Dependency.ema", ResourceKind.Macro, 'd');
        if (scenario == "referenced")
        {
            macro = macro with { Storage = ResourceStorage.Referenced, Reference = new ResourceReference { Root = "fixtures", Path = "Dependency.ema" } };
        }
        PackManifest dependency = Manifests.Minimal(macro) with { Id = "acme.modules", SchemaVersion = 2 };
        byte[] dependencyBytes = PackJson.Serialize(dependency);
        const string ModulePath = "knowledge/module/motor-starter.json";
        KnowledgeDocument module = KnowledgeDocument.Read(await File.ReadAllBytesAsync(folder.PathOf(ModulePath), TestContext.Current.CancellationToken));
        module.Spec["macro"]!["asset"] = AssetNames.Id(dependency.Id, scenario == "version" ? "2.0.0" : dependency.Version, AssetNames.Local(macro));
        module.Spec["macro"]!["hash"] = scenario == "hash" ? Manifests.Hash('f') : macro.Sha256;
        folder.Write(ModulePath, PackJson.Serialize(module));
        PackResource updated = await PackFolders.ResourceAsync(folder, ModulePath, ResourceKind.Module);
        manifest = manifest with
        {
            Dependencies = [new PackDependency { Id = dependency.Id, Version = dependency.Version }],
            Resources = [.. manifest.Resources.Select(r => r.Path == ModulePath ? updated : r)],
        };
        byte[] manifestBytes = PackJson.Serialize(manifest);
        folder.Write(PackManifest.FileName, manifestBytes);
        if (scenario != "lock")
        {
            folder.Write(PackManifest.LockFileName, PackJson.Serialize(PackLock.Create(
                new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(manifestBytes) },
                [new PackReference { Id = dependency.Id, Version = dependency.Version, Digest = scenario == "digest" ? Manifests.Hash('f') : PackDigest.Compute(dependencyBytes) }])));
        }

        PackCheckResult result = await PackVerifier.CheckDirectoryWithDependenciesAsync(folder.Root, scenario == "manifest" ? [] : [dependencyBytes], TestContext.Current.CancellationToken);

        Assert.Equal(scenario == "valid", result.Ok);
        if (!result.Ok)
        {
            PackProblem problem = Assert.Single(result.Problems);
            Assert.Equal(ModulePath, problem.Location);
            Assert.StartsWith(scenario == "hash" ? "/spec/macro/hash:" : "/spec/macro/asset:", problem.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Check_ModuleMappingSampleAndSources_ResolveThroughLockedDependency()
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder);
        PackManifest dependency = Manifests.Minimal([.. manifest.Resources.Where(r => r.Kind is not (ResourceKind.Module or ResourceKind.ConfigSchema or ResourceKind.Macro))]) with { Id = "acme.materials", SchemaVersion = 2 };
        byte[] dependencyBytes = PackJson.Serialize(dependency);
        string External(string local) => AssetNames.Id(dependency.Id, dependency.Version, local);
        var resources = new List<PackResource>();
        foreach (PackResource resource in manifest.Resources)
        {
            if (resource.Kind is ResourceKind.Module or ResourceKind.MaterialMapping or ResourceKind.Sample)
            {
                KnowledgeDocument document = KnowledgeDocument.Read(await File.ReadAllBytesAsync(folder.PathOf(resource.Path), TestContext.Current.CancellationToken));
                if (document.Kind == KnowledgeKind.Module)
                {
                    document.Spec["partMapping"]!["asset"] = External("material_mapping/company-a");
                    document.Spec["acceptance"]!["samples"]![0] = External("sample/motor-acceptance");
                    foreach (JsonNode? check in document.Spec["acceptance"]!["checks"]!.AsArray())
                    {
                        check!["source"]!["asset"] = External("document/guide.md");
                    }
                    document.Spec["extraction"]!["source"]!["asset"] = External("document/guide.md");
                }
                else if (document.Kind == KnowledgeKind.MaterialMapping)
                {
                    foreach (JsonNode? entry in document.Spec["entries"]!.AsArray())
                    {
                        entry!["source"]!["asset"] = External("document/guide.md");
                    }
                }
                else
                {
                    document.Spec["workflow"] = External("workflow/motor");
                    document.Spec["configFixture"] = External("config_layer/company");
                    document.Spec["expectedChecks"]![0] = External("rule/starter-allowed");
                    document.Spec["sources"]![0]!["asset"] = External("document/guide.md");
                }
                folder.Write(resource.Path, PackJson.Serialize(document));
                resources.Add(await PackFolders.ResourceAsync(folder, resource.Path, resource.Kind));
            }
            else
            {
                resources.Add(resource);
            }
        }
        manifest = manifest with { Resources = resources, Dependencies = [new PackDependency { Id = dependency.Id, Version = dependency.Version }] };
        byte[] manifestBytes = PackJson.Serialize(manifest);
        folder.Write(PackManifest.FileName, manifestBytes);
        folder.Write(PackManifest.LockFileName, PackJson.Serialize(PackLock.Create(
            new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(manifestBytes) },
            [new PackReference { Id = dependency.Id, Version = dependency.Version, Digest = PackDigest.Compute(dependencyBytes) }])));

        PackCheckResult result = await PackVerifier.CheckDirectoryWithDependenciesAsync(folder.Root, [dependencyBytes], TestContext.Current.CancellationToken);

        Assert.True(result.Ok, string.Join("; ", result.Problems));
        Assert.False((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Ok);
    }

    [Theory]
    [InlineData("knowledge/workflow/legacy.json")]
    [InlineData("knowledge/rule/legacy.json")]
    public async Task Check_V1ModuleAtWorkflowOrRulePath_CannotSkipValidation(string path)
    {
        using var folder = new TempFolder();
        folder.Write(path, """{"apiVersion":"eplanmcp.pack/v1","kind":"module","metadata":{"name":"legacy"},"spec":{"macro":"macro/legacy"}}""");
        folder.Write(PackManifest.FileName, PackJson.Serialize(Manifests.Minimal(await PackFolders.ResourceAsync(folder, path, ResourceKind.Module))));

        Assert.False((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Ok);
    }

    [Theory]
    [InlineData("motor-starter", "/valueSets/0/values/power", "\"7.5\"", "/spec/valueSets/0/values/power")]
    [InlineData("motor-starter", "/applicability", "{\"exists\":[{\"config\":\"unknown\"}]}", "/spec/applicability/exists/0/config")]
    [InlineData("company-a", "/entries/1/when", "{\"power\":7.5}", "/spec/entries/1/when")]
    [InlineData("motor-acceptance", "/sources/0/hash", "\"changed\"", "/spec/sources/0/hash")]
    public async Task Check_InvalidMetadataWithCorrectFileHash_IsRejected(string name, string path, string json, string expectedPointer)
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder);
        KnowledgeDocument fixture = ModuleFixtures.Document(name);
        string resourcePath = KnowledgeDocument.PathOf(fixture.Kind, name);
        KnowledgeDocument document = KnowledgeDocument.Read(await File.ReadAllBytesAsync(folder.PathOf(resourcePath), TestContext.Current.CancellationToken));
        ModuleFixtures.Set(document.Spec, path, json);
        folder.Write(resourcePath, PackJson.Serialize(document));
        await RehashAsync(folder, manifest, resourcePath);

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);

        Assert.Equal(resourcePath, problem.Location);
        Assert.StartsWith(expectedPointer + ":", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apiVersion", "\"eplanmcp.pack/v3\"", "/apiVersion")]
    [InlineData("kind", "\"sample\"", "/kind")]
    [InlineData("metadata", "{\"name\":\"other\"}", "/metadata/name")]
    [InlineData("extra", "true", "extra")]
    public async Task Check_ModuleEnvelope_ValidatesVersionKindAndName(string key, string json, string expectedPointer)
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder);
        const string Path = "knowledge/module/motor-starter.json";
        JsonObject document = JsonNode.Parse(await File.ReadAllBytesAsync(folder.PathOf(Path), TestContext.Current.CancellationToken))!.AsObject();
        document[key] = JsonNode.Parse(json);
        folder.Write(Path, PackJson.Serialize(document));
        await RehashAsync(folder, manifest, Path);

        Assert.Contains(expectedPointer, Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_ModulePathMislabeledAsDocument_CannotBypassValidation()
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder);
        const string Path = "knowledge/module/motor-starter.json";
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with
        {
            Resources = [.. manifest.Resources.Select(r => r.Path == Path ? r with { Kind = ResourceKind.Document } : r)],
        }));

        Assert.False((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Ok);
    }

    [Fact]
    public async Task Check_V2ModuleOutsideKnowledgeWithoutKind_IsRejected()
    {
        using var folder = new TempFolder();
        const string Path = "resources/modules/motor.json";
        folder.Write(Path, """{"apiVersion":"eplanmcp.pack/v2","metadata":{"name":"motor.json"},"spec":{}}""");
        folder.Write(PackManifest.FileName, PackJson.Serialize(Manifests.Minimal(await PackFolders.ResourceAsync(folder, Path, ResourceKind.Module)) with { SchemaVersion = 2 }));

        PackProblem problem = Assert.Single((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Problems);

        Assert.Equal(Path, problem.Location);
        Assert.Contains("kind", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(KnowledgeKind.Module, "{\"macro\":\"macro/legacy\",\"parameters\":[]}")]
    [InlineData(KnowledgeKind.MaterialMapping, "{\"entries\":[{\"when\":{\"power\":\"7.5\"},\"part\":\"LEGACY\"}]}")]
    public async Task Check_LegacyDocuments_StayUnchangedWithoutV2Validation(KnowledgeKind kind, string spec)
    {
        using var folder = new TempFolder();
        string path = KnowledgeDocument.PathOf(kind, "legacy");
        byte[] bytes = PackJson.Serialize(new KnowledgeDocument
        {
            ApiVersion = KnowledgeDocument.CurrentApiVersion,
            Kind = kind,
            Metadata = new KnowledgeMetadata { Name = "legacy" },
            Spec = JsonNode.Parse(spec)!.AsObject(),
        });
        folder.Write(path, bytes);
        folder.Write(PackManifest.FileName, PackJson.Serialize(Manifests.Minimal(await PackFolders.ResourceAsync(folder, path, KnowledgeDocument.ResourceKindOf(kind)))));

        Assert.True((await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken)).Ok);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(folder.PathOf(path), TestContext.Current.CancellationToken));
        Assert.Equal(KnowledgeDocument.CurrentApiVersion, KnowledgeDocument.Read(bytes).ApiVersion);
    }

    [Fact]
    public async Task Check_ReplacedSourceWithUpdatedManifest_RejectsEveryStaleEvidenceHash()
    {
        using var folder = new TempFolder();
        PackManifest manifest = await WriteAsync(folder);
        const string Path = "resources/docs/guide.md";
        folder.Write(Path, "Changed non-customer evidence.");
        await RehashAsync(folder, manifest, Path);

        PackCheckResult result = await PackVerifier.CheckDirectoryAsync(folder.Root, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Problems.Count);
        Assert.All(result.Problems, p => Assert.Contains("/source", p.Message, StringComparison.Ordinal));
    }

    private static async Task<PackManifest> WriteAsync(TempFolder folder)
    {
        const string Macro = "resources/macros/Motor.ema";
        const string Source = "resources/docs/guide.md";
        folder.Write(Macro, "Synthetic hash fixture; not an EPLAN macro.");
        folder.Write(Source, "Non-customer O2 starter table, metadata validation only.");
        PackResource macro = await PackFolders.ResourceAsync(folder, Macro, ResourceKind.Macro);
        PackResource source = await PackFolders.ResourceAsync(folder, Source, ResourceKind.Document);
        var resources = new List<PackResource> { macro, source };
        foreach (string name in new[] { "motor-starter", "company-a", "company-b", "motor-acceptance" })
        {
            KnowledgeDocument fixture = ModuleFixtures.Document(name);
            string path = KnowledgeDocument.PathOf(fixture.Kind, name);
            string json = System.Text.Encoding.UTF8.GetString(PackJson.Serialize(fixture))
                .Replace(Manifests.Hash('a'), macro.Sha256, StringComparison.Ordinal).Replace(Manifests.Hash('b'), source.Sha256, StringComparison.Ordinal);
            folder.Write(path, json);
            resources.Add(await PackFolders.ResourceAsync(folder, path, KnowledgeDocument.ResourceKindOf(fixture.Kind)));
        }

        foreach ((string path, ResourceKind kind, byte[] bytes) in new[]
        {
            (ConfigFixtures.SchemaPath, ResourceKind.ConfigSchema, ConfigFixtures.Read("MotorsPack/" + ConfigFixtures.SchemaPath)),
            (ConfigFixtures.CompanyPath, ResourceKind.ConfigLayer, ConfigFixtures.Read("MotorsPack/" + ConfigFixtures.CompanyPath)),
            ("knowledge/rule/starter-allowed.json", ResourceKind.Rule, ConfigFixtures.Read("Rules/valid.json")),
            ("knowledge/workflow/motor.json", ResourceKind.Workflow, System.Text.Encoding.UTF8.GetBytes("""{"apiVersion":"eplanmcp.pack/v2","kind":"workflow","metadata":{"name":"motor"},"spec":{"parameters":{},"steps":[{"op":"page.create","args":{"pages":[{"name":"=AP+A1/1"}]}}]}}""")),
        })
        {
            folder.Write(path, bytes);
            resources.Add(await PackFolders.ResourceAsync(folder, path, kind));
        }

        PackManifest manifest = Manifests.Minimal([.. resources]) with { SchemaVersion = 2 };
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest));
        return manifest;
    }

    private static async Task RehashAsync(TempFolder folder, PackManifest manifest, string path)
    {
        PackResource updated = await PackFolders.ResourceAsync(folder, path, manifest.Resources.Single(r => r.Path == path).Kind);
        folder.Write(PackManifest.FileName, PackJson.Serialize(manifest with { Resources = [.. manifest.Resources.Select(r => r.Path == path ? updated : r)] }));
    }
}
