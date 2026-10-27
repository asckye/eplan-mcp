using System.Text.RegularExpressions;

namespace EplanMcp.Packs;

/// <summary>
/// The rules of a manifest beyond its JSON shape (which <see cref="PackJson"/> enforces when reading): the patterns the
/// schema declares, unique ids and paths, the canonical order, and references that resolve. Reads no files; the hashes
/// are checked against the files by whoever opens the pack.
/// </summary>
public static partial class PackManifestValidator
{
    public const int MaxIdLength = 64;

    public const int MaxTextLength = 500;

    public static IReadOnlyList<PackProblem> Validate(PackManifest manifest)
    {
        var problems = new List<PackProblem>();
        void Add(string location, string message) => problems.Add(new PackProblem(location, message));

        if (manifest.SchemaVersion is not 1 and not 2)
        {
            Add("schemaVersion", "must be 1 or 2");
        }

        CheckId("id", manifest.Id, Add);
        CheckText("name", manifest.Name, required: true, Add);
        CheckText("description", manifest.Description, required: false, Add);
        if (!SemanticVersion.TryParse(manifest.Version, out _))
        {
            Add("version", "must be a SemVer 2.0.0 version, such as 1.0.0");
        }

        PackCompatibility compatibility = manifest.Compatibility;
        if (compatibility.EplanVersions is not null && !EplanVersionRange.TryParse(compatibility.EplanVersions, out _))
        {
            Add("compatibility.eplanVersions", "must be comparators such as '>=2026.0 <2027' or a version prefix such as '2026.0'");
        }

        CheckList("compatibility.variants", compatibility.Variants, v => IsText(v) ? null : "must be a name without control characters", Add);
        CheckList("compatibility.capabilities", compatibility.Capabilities, c => c.Length <= 256 && Capability().IsMatch(c) ? null
            : "must be A:<action>[/TYPE], O:<type>.<name>, op:<operation>, licence:<option>, plugin:<id> or an eplan_* tool name", Add);

        CheckList("dependencies", manifest.Dependencies.Select(d => d.Id).ToList(), id => id == manifest.Id ? "must not be the pack itself" : null, Add, StringComparer.Ordinal);
        for (int i = 0; i < manifest.Dependencies.Count; i++)
        {
            CheckId($"dependencies[{i}].id", manifest.Dependencies[i].Id, Add);
            if (!VersionRange.TryParse(manifest.Dependencies[i].Version, out _))
            {
                Add($"dependencies[{i}].version", "must be a version range such as '^1.2.0' or '>=1.2.0 <2.0.0'");
            }
        }

        var inputs = manifest.Provenance.Inputs.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var resources = new HashSet<string>(PackPath.Comparer);
        for (int i = 0; i < manifest.Resources.Count; i++)
        {
            CheckResource($"resources[{i}]", manifest.Resources[i], inputs, Add);
            if (!resources.Add(manifest.Resources[i].Path))
            {
                Add($"resources[{i}].path", "names the same file as an earlier resource (paths are compared ignoring case)");
            }
        }

        // A project's data folder belongs to the project's resource, so no other resource may lie inside it.
        foreach (PackResource project in manifest.Resources.Where(r => r.Kind == ResourceKind.Project && r.Path.Length > 4))
        {
            string folder = project.Path[..^4] + ".edb/";
            foreach (PackResource inside in manifest.Resources.Where(r => r.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            {
                Add("resources", $"'{inside.Path}' lies in the data folder of project '{project.Path}'");
            }
        }

        foreach ((string name, IReadOnlyList<PackSlot> slots) in new[]
        {
            ("rules", manifest.Rules), ("mappings", manifest.Mappings), ("workflows", manifest.Workflows), ("docs", manifest.Docs),
            ("acceptanceSamples", manifest.AcceptanceSamples),
        })
        {
            CheckList(name, slots.Select(s => s.Path).ToList(), p => resources.Contains(p) ? null : "is not a resource of this pack", Add, PackPath.Comparer);
            for (int i = 0; i < slots.Count; i++)
            {
                CheckText($"{name}[{i}].note", slots[i].Note, required: false, Add);
            }
        }

        PackConfiguration configuration = manifest.Configuration;
        CheckScopes("configuration.base", configuration.Base is { } baseScope ? [baseScope] : [], resources, Add);
        CheckScopes("configuration.company", configuration.Company is { } company ? [company] : [], resources, Add);
        CheckScopes("configuration.cabinetTypes", configuration.CabinetTypes, resources, Add);
        for (int i = 0; i < manifest.Migrations.Count; i++)
        {
            PackMigration migration = manifest.Migrations[i];
            if (!VersionRange.TryParse(migration.From, out _))
            {
                Add($"migrations[{i}].from", "must be a version range such as '^1.0.0'");
            }

            CheckText($"migrations[{i}].notes", migration.Notes, required: false, Add);
            CheckList($"migrations[{i}].changes", migration.Changes, c => IsText(c) ? null : "must be one line of text", Add);
        }

        for (int i = 0; i < manifest.Facts.Count; i++)
        {
            PackFact fact = manifest.Facts[i];
            if (!resources.Contains(fact.Resource))
            {
                Add($"facts[{i}].resource", "is not a resource of this pack");
            }

            if (!Name().IsMatch(fact.Name))
            {
                Add($"facts[{i}].name", "must be a snake_case name");
            }

            CheckText($"facts[{i}].value", fact.Value, required: true, Add);
            CheckText($"facts[{i}].evidence", fact.Evidence, required: true, Add);
        }

        CheckProvenance(manifest.Provenance, Add);
        CheckOrder(manifest, Add);
        return problems;
    }

    private static void CheckResource(string location, PackResource resource, HashSet<string> inputs, Action<string, string> add)
    {
        if (PackPath.Problem(resource.Path) is { } problem)
        {
            add($"{location}.path", $"'{resource.Path}' {problem}");
        }
        else if (ResourceTypes.IsPackFile(resource.Path))
        {
            add($"{location}.path", $"'{resource.Path}' is the name of a pack file");
        }
        else if (ResourceTypes.Problem(resource.Kind, resource.Path) is { } typeProblem)
        {
            add($"{location}.path", $"'{resource.Path}' {typeProblem}");
        }

        if (!Sha256().IsMatch(resource.Sha256))
        {
            add($"{location}.sha256", "must be 64 lowercase hex digits");
        }

        if (resource.Bytes < 0)
        {
            add($"{location}.bytes", "must not be negative");
        }

        bool project = resource.Kind == ResourceKind.Project;
        if (resource.Files < 1 || (!project && resource.Files != 1))
        {
            add($"{location}.files", project ? "must be at least 1" : "must be 1 for a single file");
        }

        if (resource.Subtype is not null && !Name().IsMatch(resource.Subtype))
        {
            add($"{location}.subtype", "must be a snake_case name");
        }

        if (resource.EplanVersion is not null && !EplanVersionRange.TryParseVersion(resource.EplanVersion, out _))
        {
            add($"{location}.eplanVersion", "must be one to four numbers, such as 2026.0.3");
        }

        if (resource.Source is { } source)
        {
            if (!inputs.Contains(source.Input))
            {
                add($"{location}.source.input", "is not in provenance.inputs");
            }

            if (source.Path.Length > 0 && PackPath.Problem(source.Path) is { } sourceProblem)
            {
                add($"{location}.source.path", sourceProblem);
            }
        }

        CheckReference(location, resource, add);
    }

    /// <summary>A referenced resource names a packSources root and a relative path below it (ADR-0024 section 6).</summary>
    private static void CheckReference(string location, PackResource resource, Action<string, string> add)
    {
        if ((resource.Storage == ResourceStorage.Referenced) != (resource.Reference is not null))
        {
            add($"{location}.reference", "is required for a referenced resource and not allowed for a bundled one");
        }
        else if (resource.Reference is { } reference)
        {
            CheckId($"{location}.reference.root", reference.Root, add);
            if ((PackPath.Problem(reference.Path) ?? (reference.Path.Contains("$(", StringComparison.Ordinal) ? "names an EPLAN variable" : null)) is { } problem)
            {
                add($"{location}.reference.path", $"must be a path below the root: it {problem}");
            }
        }
    }

    private static void CheckScopes(string location, IReadOnlyList<PackScope> scopes, HashSet<string> resources, Action<string, string> add)
    {
        CheckList(location, scopes.Select(s => s.Id).ToList(), _ => null, add, StringComparer.Ordinal);
        for (int i = 0; i < scopes.Count; i++)
        {
            CheckId($"{location}[{i}].id", scopes[i].Id, add);
            CheckText($"{location}[{i}].name", scopes[i].Name, required: false, add);
            CheckList($"{location}[{i}].resources", scopes[i].Resources, p => resources.Contains(p) ? null : "is not a resource of this pack", add, PackPath.Comparer);
        }
    }

    private static void CheckProvenance(PackProvenance provenance, Action<string, string> add)
    {
        CheckText("provenance.createdBy", provenance.CreatedBy, required: true, add);
        CheckText("provenance.author", provenance.Author, required: false, add);
        if (provenance.CreatedAt.Offset != TimeSpan.Zero)
        {
            add("provenance.createdAt", "must be UTC (offset +00:00)");
        }

        CheckList("provenance.inputs", provenance.Inputs.Select(i => i.Name).ToList(),
            n => n.Contains('/', StringComparison.Ordinal) || PackPath.Problem(n) is not null ? "must be one file or folder name" : null, add, StringComparer.Ordinal);
    }

    /// <summary>The order the generator writes, so one pack has one manifest.</summary>
    private static void CheckOrder(PackManifest manifest, Action<string, string> add)
    {
        if (!IsSorted(manifest.Resources.Select(r => r.Path)))
        {
            add("resources", "must be sorted by path (ordinal)");
        }

        if (!IsSorted(manifest.Dependencies.Select(d => d.Id)))
        {
            add("dependencies", "must be sorted by id");
        }
    }

    private static bool IsSorted(IEnumerable<string> values) =>
        values.Zip(values.Skip(1)).All(pair => string.CompareOrdinal(pair.First, pair.Second) < 0);

    /// <summary>Each value is unique (by <paramref name="comparer"/>) and passes <paramref name="check"/>.</summary>
    private static void CheckList(string location, IReadOnlyList<string> values, Func<string, string?> check, Action<string, string> add, IEqualityComparer<string>? comparer = null)
    {
        var seen = new HashSet<string>(comparer ?? StringComparer.Ordinal);
        for (int i = 0; i < values.Count; i++)
        {
            if (check(values[i]) is { } problem)
            {
                add($"{location}[{i}]", $"'{values[i]}' {problem}");
            }

            if (!seen.Add(values[i]))
            {
                add($"{location}[{i}]", $"'{values[i]}' is listed twice");
            }
        }
    }

    private static void CheckId(string location, string id, Action<string, string> add)
    {
        if (id.Length > MaxIdLength || !Id().IsMatch(id))
        {
            add(location, $"must be at most {MaxIdLength} lowercase letters and digits in groups joined by '.' or '-', such as acme.cabinet-base");
        }
    }

    private static void CheckText(string location, string? text, bool required, Action<string, string> add)
    {
        if (text is null ? required : !IsText(text))
        {
            add(location, $"must be 1 to {MaxTextLength} characters without control characters");
        }
    }

    private static bool IsText(string text) => text.Length is > 0 and <= MaxTextLength && !text.Any(char.IsControl);

    /// <summary>Whether <paramref name="id"/> is a pack id; names of knowledge documents and pack sources follow the same rule.</summary>
    public static bool IsId(string id) => id.Length <= MaxIdLength && Id().IsMatch(id);

    internal static bool IsSha256(string hash) => Sha256().IsMatch(hash);

    [GeneratedRegex(PackPatterns.Id, RegexOptions.ECMAScript)]
    private static partial Regex Id();

    [GeneratedRegex(PackPatterns.Sha256, RegexOptions.ECMAScript)]
    private static partial Regex Sha256();

    [GeneratedRegex(PackPatterns.Capability, RegexOptions.ECMAScript)]
    private static partial Regex Capability();

    [GeneratedRegex(PackPatterns.Name, RegexOptions.ECMAScript)]
    private static partial Regex Name();
}
