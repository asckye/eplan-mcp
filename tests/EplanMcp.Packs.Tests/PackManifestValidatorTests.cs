namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackManifestValidatorTests
{
    [Fact]
    public void Validate_CompleteManifest_HasNoProblems()
    {
        Assert.Empty(PackManifestValidator.Validate(Manifests.Complete()));
    }

    public static TheoryData<string, Func<PackManifest, PackManifest>> Broken => new()
    {
        { "schemaVersion", m => m with { SchemaVersion = 2 } },
        { "id", m => m with { Id = "ACME" } },
        { "id", m => m with { Id = "acme..base" } },
        { "id", m => m with { Id = new string('a', 65) } },
        { "name", m => m with { Name = "" } },
        { "name", m => m with { Name = "two\nlines" } },
        { "version", m => m with { Version = "1.0" } },
        { "version", m => m with { Version = "01.0.0" } },
        { "compatibility.eplanVersions", m => m with { Compatibility = new PackCompatibility { EplanVersions = ">=2026.x" } } },
        { "compatibility.variants[1]", m => m with { Compatibility = new PackCompatibility { Variants = ["Pro Panel", "Pro Panel"] } } },
        { "compatibility.capabilities[0]", m => m with { Compatibility = new PackCompatibility { Capabilities = ["run anything"] } } },
        { "dependencies[0]", m => m with { Dependencies = [new PackDependency { Id = m.Id, Version = "*" }] } },
        { "dependencies[1]", m => m with { Dependencies = [new PackDependency { Id = "a.b", Version = "*" }, new PackDependency { Id = "a.b", Version = "*" }] } },
        { "dependencies[0].version", m => m with { Dependencies = [new PackDependency { Id = "a.b", Version = "1.x" }] } },
        { "dependencies", m => m with { Dependencies = [new PackDependency { Id = "b.b", Version = "*" }, new PackDependency { Id = "a.b", Version = "*" }] } },
        { "resources[0].path", m => Replace(m, 0, r => r with { Path = "../escape.zw9" }) },
        { "resources[0].path", m => Replace(m, 0, r => r with { Path = "resources\\basic.zw9" }) },
        { "resources[0].path", m => Replace(m, 0, r => r with { Path = "/rooted.zw9" }) },
        { "resources[0].path", m => Replace(m, 0, r => r with { Path = "pack.json" }) },
        { "resources[0].sha256", m => Replace(m, 0, r => r with { Sha256 = new string('A', 64) }) },
        { "resources[0].bytes", m => Replace(m, 0, r => r with { Bytes = -1 }) },
        { "resources[0].files", m => Replace(m, 0, r => r with { Files = 2 }) },
        { "resources[0].subtype", m => Replace(m, 0, r => r with { Subtype = "Window Macro" }) },
        { "resources[0].eplanVersion", m => Replace(m, 0, r => r with { EplanVersion = "2026.0.3.1.5" }) },
        { "resources[0].source.input", m => Replace(m, 0, r => r with { Source = new ResourceSource { Input = "elsewhere", Path = "x" } }) },
        { "resources[0].source.path", m => Replace(m, 0, r => r with { Source = new ResourceSource { Input = "inputs", Path = "C:/x" } }) },
        { "resources[1].path", m => m with { Resources = [Manifests.File("a/x.ema", ResourceKind.Macro), Manifests.File("a/x.emA", ResourceKind.Macro)] } },
        { "resources", m => m with { Resources = [Manifests.File("b.ema", ResourceKind.Macro), Manifests.File("a.ema", ResourceKind.Macro)] } },
        { "resources[0].path", m => m with { Resources = [Manifests.File("p/Sample.zw1", ResourceKind.Project) with { Files = 3 }] } },
        { "resources", m => m with { Resources = [Manifests.File("p/S.edb/Extra.ema", ResourceKind.Macro), Manifests.File("p/S.elk", ResourceKind.Project) with { Files = 3 }] } },
        { "rules[0]", m => m with { Rules = [new PackSlot { Path = "resources/rules/missing.md", Standing = KnowledgeStanding.Official }] } },
        { "mappings[1]", m => m with { Mappings = [.. m.Mappings, m.Mappings[0]] } },
        { "configuration.cabinetTypes[0].id", m => m with { Configuration = new PackConfiguration { CabinetTypes = [new PackScope { Id = "TS 8" }] } } },
        { "configuration.projects[0].resources[0]", m => m with { Configuration = new PackConfiguration { Projects = [new PackScope { Id = "p1", Resources = ["missing"] }] } } },
        { "facts[0].resource", m => m with { Facts = [m.Facts[0] with { Resource = "missing" }] } },
        { "facts[0].name", m => m with { Facts = [m.Facts[0] with { Name = "EPLAN version" }] } },
        { "provenance.createdAt", m => m with { Provenance = m.Provenance with { CreatedAt = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.FromHours(8)) } } },
        { "provenance.inputs[0]", m => m with { Provenance = m.Provenance with { Inputs = [new PackInput { Name = "a/b", Kind = PackInputKind.Folder }] } } },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void Validate_BrokenManifest_NamesTheLocation(string location, Func<PackManifest, PackManifest> breakIt)
    {
        IReadOnlyList<PackProblem> problems = PackManifestValidator.Validate(breakIt(Manifests.Complete()));

        Assert.Contains(problems, p => p.Location == location);
    }

    private static PackManifest Replace(PackManifest manifest, int index, Func<PackResource, PackResource> change) =>
        manifest with { Resources = [.. manifest.Resources.Select((r, i) => i == index ? change(r) : r)] };
}
