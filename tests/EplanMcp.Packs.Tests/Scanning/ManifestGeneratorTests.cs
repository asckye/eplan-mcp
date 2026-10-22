using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs.Tests.Scanning;

[Trait("Size", "Small")]
public sealed class ManifestGeneratorTests
{
    private static readonly PackIdentity _identity = new("acme.cabinet-base", "ACME cabinet base", "1.0.0", Author: "Wang", Variants: ["Electric P8"]);

    [Fact]
    public void Generate_FillsSlotsFactsAndProvenanceInCanonicalOrder()
    {
        var rules = new ScanInput("rules", ResourceKind.Rule, Official: true);
        var samples = new ScanInput("samples", AcceptanceSample: true);
        var plain = new ScanInput("inputs");
        ScanResult scan = new(
            [new PackInput { Name = "rules", Kind = PackInputKind.Folder }, new PackInput { Name = "samples", Kind = PackInputKind.Folder }, new PackInput { Name = "inputs", Kind = PackInputKind.Folder }],
            [
                Scanned("resources/rules/rules/naming.md", ResourceKind.Rule, rules, "rules"),
                Scanned("resources/projects/samples/Plant.elk", ResourceKind.Project, samples, "samples", new PackFact { Resource = "resources/projects/samples/Plant.elk", Name = "eplan_version", Value = "2026.0.3", Evidence = "ProjectInfo.xml property 10043" }),
                Scanned("resources/mappings/inputs/parts.csv", ResourceKind.MaterialMapping, plain, "inputs"),
                Scanned("resources/docs/inputs/readme.md", ResourceKind.Document, plain, "inputs"),
            ],
            []);

        PackManifest manifest = ManifestGenerator.Generate(scan, _identity, "eplanmcp 0.1.0", new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.FromHours(8)));

        Assert.Empty(PackManifestValidator.Validate(manifest));
        Assert.Equal(["resources/docs/inputs/readme.md", "resources/mappings/inputs/parts.csv", "resources/projects/samples/Plant.elk", "resources/rules/rules/naming.md"], manifest.Resources.Select(r => r.Path));
        Assert.Equal(KnowledgeStanding.Official, Assert.Single(manifest.Rules).Standing);
        Assert.Equal(KnowledgeStanding.Candidate, Assert.Single(manifest.Mappings).Standing);
        Assert.Equal(KnowledgeStanding.Candidate, Assert.Single(manifest.Docs).Standing);
        Assert.Equal("resources/projects/samples/Plant.elk", Assert.Single(manifest.AcceptanceSamples).Path);
        Assert.Equal("eplan_version", Assert.Single(manifest.Facts).Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), manifest.Provenance.CreatedAt);
        Assert.Equal(TimeSpan.Zero, manifest.Provenance.CreatedAt.Offset);
        Assert.Null(manifest.Compatibility.EplanVersions);
        Assert.Equal(["Electric P8"], manifest.Compatibility.Variants);
    }

    [Fact]
    public void Generate_OfficialFolderWithAGuessedMapping_KeepsTheGuessACandidate()
    {
        var rules = new ScanInput("rules", ResourceKind.Rule, Official: true);
        ScanResult scan = new([new PackInput { Name = "rules", Kind = PackInputKind.Folder }], [Scanned("resources/mappings/rules/parts.csv", ResourceKind.MaterialMapping, rules, "rules")], []);

        PackManifest manifest = ManifestGenerator.Generate(scan, _identity, "eplanmcp 0.1.0", Manifests.CreatedAt);

        Assert.Equal(KnowledgeStanding.Candidate, Assert.Single(manifest.Mappings).Standing);
    }

    private static ScannedResource Scanned(string path, ResourceKind kind, ScanInput input, string inputName, params PackFact[] facts) =>
        new(Manifests.File(path, kind) with { Source = new ResourceSource { Input = inputName, Path = path.Split('/')[^1] }, Files = kind == ResourceKind.Project ? 3 : 1 }, facts, input, []);
}
