namespace EplanMcp.Packs.Scanning;

/// <summary>
/// The manifest of a scan: the engineer gives only the identity; resources, facts, slots and provenance come from the
/// scan, in the canonical order. A rule or mapping is official only when its input was given with that kind and marked
/// official by the engineer; otherwise it is a candidate. Acceptance samples are the projects the engineer marked.
/// </summary>
public static class ManifestGenerator
{
    public static PackManifest Generate(ScanResult scan, PackIdentity identity, string createdBy, DateTimeOffset createdAt)
    {
        List<ScannedResource> resources = [.. scan.Resources.OrderBy(r => r.Resource.Path, StringComparer.Ordinal)];
        return new PackManifest
        {
            SchemaVersion = PackManifest.CurrentSchemaVersion,
            Id = identity.Id,
            Name = identity.Name,
            Version = identity.Version,
            Description = identity.Description,
            Compatibility = new PackCompatibility { EplanVersions = identity.EplanVersions, Variants = identity.Variants ?? [] },
            Resources = [.. resources.Select(r => r.Resource)],
            Rules = Slots(resources, ResourceKind.Rule),
            Mappings = Slots(resources, ResourceKind.MaterialMapping),
            Docs = Slots(resources, ResourceKind.Document),
            AcceptanceSamples =
            [
                .. resources.Where(r => r.Input.AcceptanceSample && r.Resource.Kind is ResourceKind.Project or ResourceKind.ProjectBackup or ResourceKind.BasicProject)
                    .Select(r => new PackSlot { Path = r.Resource.Path, Standing = KnowledgeStanding.Official }),
            ],
            Facts =
            [
                .. resources.SelectMany(r => r.Facts)
                    .OrderBy(f => f.Resource, StringComparer.Ordinal).ThenBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Evidence, StringComparer.Ordinal),
            ],
            Provenance = new PackProvenance
            {
                CreatedBy = createdBy,
                Author = identity.Author,
                CreatedAt = createdAt.ToUniversalTime(),
                Inputs = scan.Inputs,
            },
        };
    }

    private static List<PackSlot> Slots(List<ScannedResource> resources, ResourceKind kind) =>
    [
        .. resources.Where(r => r.Resource.Kind == kind).Select(r => new PackSlot
        {
            Path = r.Resource.Path,
            Standing = r.Input.Official && r.Input.Kind == kind ? KnowledgeStanding.Official : KnowledgeStanding.Candidate,
        }),
    ];
}
