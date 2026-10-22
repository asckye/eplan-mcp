using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("The manifest of an EplanMcp engineering pack (pack.json at the pack's root). EplanMcp generates it; nobody writes it by hand.")]
public sealed record PackManifest
{
    /// <summary>The only <see cref="SchemaVersion"/> this code reads and writes; a reader refuses any other.</summary>
    public const int CurrentSchemaVersion = 1;

    public const string FileName = "pack.json";

    /// <summary>The lock file beside the manifest: the exact dependency versions resolved, with their hashes.</summary>
    public const string LockFileName = "pack.lock.json";

    [Description("The manifest format. A reader refuses a version it does not know instead of guessing.")]
    [Schema(Const = CurrentSchemaVersion)]
    public required int SchemaVersion { get; init; }

    [Description("The pack's identity: lowercase letters and digits in groups joined by '.' or '-', such as acme.cabinet-base.")]
    [Schema(Pattern = PackPatterns.Id)]
    public required string Id { get; init; }

    [Description("The name shown to engineers.")]
    public required string Name { get; init; }

    [Description("The pack's version, SemVer 2.0.0. An installed version never changes; a change is a new version.")]
    [Schema(Pattern = PackPatterns.SemVer)]
    public required string Version { get; init; }

    public string? Description { get; init; }

    public required PackCompatibility Compatibility { get; init; }

    [Description("Other packs this one needs; pack.lock.json records the exact versions resolved. Sorted by id.")]
    public IReadOnlyList<PackDependency> Dependencies { get; init; } = [];

    [Description("Every file and project in the pack, sorted by path. EPLAN files are stored unchanged.")]
    public required IReadOnlyList<PackResource> Resources { get; init; }

    public PackConfiguration Configuration { get; init; } = new();

    [Description("Rule files.")]
    public IReadOnlyList<PackSlot> Rules { get; init; } = [];

    [Description("Material mapping files: the company's material numbers against EPLAN parts.")]
    public IReadOnlyList<PackSlot> Mappings { get; init; } = [];

    [Description("Workflow files; reserved until a workflow format exists.")]
    public IReadOnlyList<PackSlot> Workflows { get; init; } = [];

    [Description("Documents for engineers and agents.")]
    public IReadOnlyList<PackSlot> Docs { get; init; } = [];

    [Description("Finished projects that show what the pack's results should look like.")]
    public IReadOnlyList<PackSlot> AcceptanceSamples { get; init; } = [];

    [Description("What EplanMcp read from the resources' own files, with the place it was read. Facts are measured, never inferred.")]
    public IReadOnlyList<PackFact> Facts { get; init; } = [];

    public required PackProvenance Provenance { get; init; }
}
