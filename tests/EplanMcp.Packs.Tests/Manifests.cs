namespace EplanMcp.Packs.Tests;

/// <summary>Manifests for the tests: <see cref="Complete"/> fills every section and passes the validator.</summary>
internal static class Manifests
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public static string Hash(char digit) => new(digit, 64);

    public static PackResource File(string path, ResourceKind kind, char hash = 'a') => new()
    {
        Path = path,
        Kind = kind,
        Sha256 = Hash(hash),
        Bytes = 10,
        Files = 1,
        Storage = ResourceStorage.Bundled,
        Source = new ResourceSource { Input = "inputs", Path = path.Split('/')[^1] },
    };

    public static PackManifest Minimal(params PackResource[] resources) => new()
    {
        SchemaVersion = PackManifest.CurrentSchemaVersion,
        Id = "acme.cabinet-base",
        Name = "ACME cabinet base",
        Version = "1.0.0",
        Compatibility = new PackCompatibility(),
        Resources = [.. resources.OrderBy(r => r.Path, StringComparer.Ordinal)],
        Provenance = new PackProvenance
        {
            CreatedBy = "eplanmcp 0.1.0",
            CreatedAt = CreatedAt,
            Inputs = [new PackInput { Name = "inputs", Kind = PackInputKind.Folder }],
        },
    };

    public static PackManifest Complete() => Minimal(
            File("resources/basic-projects/GB_bas001.zw9", ResourceKind.BasicProject, 'b'),
            new PackResource
            {
                Path = "resources/projects/Sample/Sample.elk",
                Kind = ResourceKind.Project,
                Sha256 = Hash('c'),
                Bytes = 2048,
                Files = 12,
                Storage = ResourceStorage.Bundled,
                Source = new ResourceSource { Input = "inputs", Path = "Sample/Sample.elk" },
                EplanVersion = "2026.0.1.25370",
            },
            File("resources/macros/Motor.ema", ResourceKind.Macro, 'd') with { Subtype = "window_macro", EplanVersion = "2026.0.25356" },
            File("resources/mappings/parts.csv", ResourceKind.MaterialMapping, 'e'),
            File("resources/rules/naming.md", ResourceKind.Rule, 'f'),
            File("resources/docs/readme.md", ResourceKind.Document, '1'),
            File("resources/other/library.ema", ResourceKind.Macro, '2') with { Storage = ResourceStorage.Referenced })
        with
    {
        Description = "Cabinets of the ACME line.",
        Compatibility = new PackCompatibility
        {
            EplanVersions = ">=2026.0 <2027",
            Variants = ["Electric P8", "Pro Panel"],
            Capabilities = ["A:backup/PROJECT", "eplan_macro_insert"],
        },
        Dependencies = [new PackDependency { Id = "acme.base", Version = "^1.2.0" }, new PackDependency { Id = "acme.parts", Version = ">=2.0.0 <3.0.0" }],
        Configuration = new PackConfiguration
        {
            Company = new PackScope { Id = "acme", Name = "ACME", Resources = ["resources/rules/naming.md"] },
            CabinetTypes = [new PackScope { Id = "ts8", Resources = ["resources/basic-projects/GB_bas001.zw9"] }],
        },
        Rules = [new PackSlot { Path = "resources/rules/naming.md", Standing = KnowledgeStanding.Official, Note = "Device tags" }],
        Mappings = [new PackSlot { Path = "resources/mappings/parts.csv", Standing = KnowledgeStanding.Candidate }],
        Docs = [new PackSlot { Path = "resources/docs/readme.md", Standing = KnowledgeStanding.Official }],
        AcceptanceSamples = [new PackSlot { Path = "resources/projects/Sample/Sample.elk", Standing = KnowledgeStanding.Official }],
        Facts = [new PackFact { Resource = "resources/projects/Sample/Sample.elk", Name = "eplan_version", Value = "2026.0.1.25370", Evidence = "ProjectInfo.xml property 10088" }],
        Provenance = new PackProvenance
        {
            CreatedBy = "eplanmcp 0.1.0",
            Author = "Wang",
            CreatedAt = CreatedAt,
            Inputs = [new PackInput { Name = "inputs", Kind = PackInputKind.Folder }],
        },
    };
}
