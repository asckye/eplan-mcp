using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs;

/// <summary>
/// Checks a pack in its directory form: <c>pack.json</c> reads and validates, every bundled resource is there with the
/// hash, size and file count the manifest records, SVGs hold no script and official rules name their source, the lock
/// (when present) fits the manifest, and nothing else is in the folder (ADR-0024). A symbolic link or junction anywhere
/// in the pack fails the check, and so does a <c>pack.sig</c> until signatures can be verified. Reads only.
/// </summary>
public static class PackVerifier
{
    public static Task<PackCheckResult> CheckDirectoryAsync(string directory, CancellationToken cancellationToken) => CheckDirectoryWithDependenciesAsync(directory, [], cancellationToken);
    public static async Task<PackCheckResult> CheckDirectoryWithDependenciesAsync(string directory, IReadOnlyList<byte[]> dependencyManifests, CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(directory);
        var files = new List<(string Relative, string FullPath)>();
        try
        {
            if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return PackCheckResult.Failed("pack", "is not a folder (or is a link to one)");
            }

            PackHasher.Collect(root, string.Empty, files);
        }
        catch (PackFormatException ex)
        {
            return PackCheckResult.Failed("pack", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return PackCheckResult.Failed("pack", "a file or folder in it cannot be read");
        }

        var present = new Dictionary<string, string>(PackPath.Comparer);
        foreach ((string relative, string fullPath) in files)
        {
            present.TryAdd(relative, fullPath);
        }

        if (!present.TryGetValue(PackManifest.FileName, out string? manifestPath))
        {
            return PackCheckResult.Failed(PackManifest.FileName, "is missing: the folder is not a pack");
        }

        try
        {
            byte[] manifestBytes = await ReadSmallAsync(manifestPath, PackManifest.FileName, cancellationToken).ConfigureAwait(false);
            PackManifest manifest = PackJson.ReadManifest(manifestBytes);
            IReadOnlyList<PackProblem> problems = PackManifestValidator.Validate(manifest);
            return problems.Count > 0
                ? new PackCheckResult(manifest, null, problems)
                : await CheckContentAsync(manifest, PackDigest.Compute(manifestBytes), present, dependencyManifests, cancellationToken).ConfigureAwait(false);
        }
        catch (PackFormatException ex)
        {
            return PackCheckResult.Failed(PackManifest.FileName, ex.Message);
        }
    }

    private static async Task<PackCheckResult> CheckContentAsync(PackManifest manifest, string manifestSha256, Dictionary<string, string> present, IReadOnlyList<byte[]>? dependencyManifests, CancellationToken cancellationToken)
    {
        var problems = new List<PackProblem>();
        var listed = new HashSet<string>(PackPath.Comparer) { PackManifest.FileName };
        PackLock? lockFile = null;
        if (present.TryGetValue(PackManifest.LockFileName, out string? lockPath))
        {
            listed.Add(PackManifest.LockFileName);
            try
            {
                lockFile = PackLock.Read(await ReadSmallAsync(lockPath, PackManifest.LockFileName, cancellationToken).ConfigureAwait(false));
                problems.AddRange(lockFile.Problems(manifest, manifestSha256));
            }
            catch (PackFormatException ex)
            {
                problems.Add(new PackProblem(PackManifest.LockFileName, ex.Message));
            }
        }

        if (present.ContainsKey(ResourceTypes.SignatureFileName))
        {
            listed.Add(ResourceTypes.SignatureFileName);
            problems.Add(new PackProblem(ResourceTypes.SignatureFileName,
                "cannot be verified by this EplanMcp yet; ADR-0024 refuses a signature it cannot verify instead of treating the pack as unsigned"));
        }

        foreach (PackResource resource in manifest.Resources)
        {
            List<KeyValuePair<string, string>> own = [.. present.Where(p => Belongs(resource, p.Key))];
            listed.UnionWith(own.Select(o => o.Key));
            bool officialRule = manifest.Rules.Any(r => r.Standing == KnowledgeStanding.Official && PackPath.Comparer.Equals(r.Path, resource.Path));
            if (resource.Storage == ResourceStorage.Referenced)
            {
                if (resource.Kind is ResourceKind.ConfigSchema or ResourceKind.ConfigLayer or ResourceKind.Workflow || AssetNames.KindOf(AssetNames.Local(resource)) is "config_schema" or "config_layer" or "workflow" || resource.Path.StartsWith("knowledge/rule/", StringComparison.Ordinal))
                {
                    problems.Add(new PackProblem(resource.Path, "/spec: configuration, rule and workflow knowledge documents must be bundled for validation"));
                }

                if (own.Count > 0 || officialRule)
                {
                    problems.Add(new PackProblem(resource.Path, officialRule ? "is an official rule, which must be in the pack so its source can be checked" : "is referenced, but its files are in the pack"));
                }
            }
            else if (!own.Any(o => PackPath.Comparer.Equals(o.Key, resource.Path)))
            {
                problems.Add(new PackProblem(resource.Path, "is missing from the pack"));
            }
            else if (await MeasureAsync(resource, own, cancellationToken).ConfigureAwait(false) is { } found
                && (found.Sha256 != resource.Sha256 || found.Bytes != resource.Bytes || found.Files != resource.Files))
            {
                problems.Add(new PackProblem(resource.Path, $"does not match pack.json: the files have SHA-256 {found.Sha256}, {found.Bytes} bytes, {found.Files} files"));
            }
            else if (await ContentProblemAsync(resource, own.First(o => PackPath.Comparer.Equals(o.Key, resource.Path)).Value, officialRule, manifest, present, Resolve, cancellationToken).ConfigureAwait(false) is { } problem)
            {
                problems.Add(new PackProblem(resource.Path, problem));
            }
        }

        problems.AddRange(present.Keys.Where(k => !listed.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => new PackProblem(k, ResourceTypes.Forbidden(k) ?? "is in the pack but not in pack.json")));
        return new PackCheckResult(manifest, lockFile, problems);

        PackResource? Resolve(string reference, ResourceKind kind)
        {
            bool exact = AssetNames.TryParse(reference, out string? id, out string? version, out string? local);
            PackManifest? owner = !exact || id == manifest.Id && version == manifest.Version ? manifest : (dependencyManifests ?? [])
                .Select(bytes => (Manifest: PackJson.ReadManifest(bytes), Digest: PackDigest.Compute(bytes)))
                .Where(p => p.Manifest.Id == id && p.Manifest.Version == version && lockFile?.Dependencies.Any(d => d.Id == id && d.Version == version && d.Digest == p.Digest) == true
                    && PackManifestValidator.Validate(p.Manifest).Count == 0).Select(p => p.Manifest).FirstOrDefault();
            return owner?.Resources.FirstOrDefault(r => r.Kind == kind && r.Storage == ResourceStorage.Bundled && AssetNames.Local(r) == (exact ? local : reference)
                && (kind != ResourceKind.Macro || r.Path.EndsWith(".ema", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static async Task<string?> ContentProblemAsync(PackResource resource, string fullPath, bool officialRule, PackManifest manifest, Dictionary<string, string> present, Func<string, ResourceKind, PackResource?> resolve, CancellationToken cancellationToken)
    {
        bool module = resource.Kind is ResourceKind.Module or ResourceKind.MaterialMapping or ResourceKind.Sample || AssetNames.KindOf(AssetNames.Local(resource)) is "module" or "material_mapping" or "sample";
        if (resource.Kind == ResourceKind.Workflow || resource.Path.StartsWith("knowledge/workflow/", StringComparison.Ordinal) || resource.Path.StartsWith("knowledge/rule/", StringComparison.Ordinal) || (module || resource.Kind == ResourceKind.Rule) && resource.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                byte[] bytes = await ReadSmallAsync(fullPath, resource.Path, cancellationToken).ConfigureAwait(false);
                if (module && AssetNames.KindOf(AssetNames.Local(resource)) is "module" or "material_mapping" && resource.Path.StartsWith("knowledge/", StringComparison.Ordinal) && KnowledgeDocument.Read(bytes).ApiVersion == KnowledgeDocument.CurrentApiVersion)
                {
                    return null;
                }
                using JsonDocument envelope = PackJson.Parse(bytes, resource.Path);
                if (resource.Kind == ResourceKind.Workflow || resource.Path.StartsWith("knowledge/workflow/", StringComparison.Ordinal) || module && resource.Path.StartsWith("knowledge/", StringComparison.Ordinal) || resource.Path.StartsWith("knowledge/rule/", StringComparison.Ordinal) || envelope.RootElement.ValueKind == JsonValueKind.Object
                    && envelope.RootElement.TryGetProperty("apiVersion", out JsonElement version) && version.ValueKind == JsonValueKind.String && version.GetString() != KnowledgeDocument.CurrentApiVersion
                    && (module || envelope.RootElement.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() is "rule" or "workflow" or "module" or "material_mapping" or "sample"))
                {
                    KnowledgeDocument rule = KnowledgeDocument.Read(bytes);
                    BindingReadModel.Require(KnowledgeDocument.ResourceKindOf(rule.Kind) == resource.Kind && (module ? rule.Kind is KnowledgeKind.Module or KnowledgeKind.MaterialMapping or KnowledgeKind.Sample : rule.Kind is KnowledgeKind.Rule or KnowledgeKind.Workflow), "/kind", "must match the resource kind");
                    BindingReadModel.Require(AssetNames.Local(resource) == KnowledgeDocument.Folder(rule.Kind) + "/" + rule.Metadata.Name, "/metadata/name", "must match the resource id");
                    var types = new Dictionary<string, RuleValueType>(StringComparer.Ordinal);
                    var items = new JsonObject();
                    foreach (PackResource source in manifest.Resources.Where(r => r.Kind == ResourceKind.ConfigSchema && r.Storage == ResourceStorage.Bundled && present.ContainsKey(r.Path)))
                    {
                        KnowledgeDocument schema = KnowledgeDocument.Read(await ReadSmallAsync(present[source.Path], source.Path, cancellationToken).ConfigureAwait(false));
                        BindingReadModel.Require(schema.Kind == KnowledgeKind.ConfigSchema, "/spec", "configuration types require config_schema documents");
                        JsonElement schemaSpec = JsonSerializer.SerializeToElement(schema.Spec, PackJson.Options);
                        ConfigValidator.Schema(schemaSpec);
                        foreach (JsonProperty item in schemaSpec.GetProperty("items").EnumerateObject())
                        {
                            string type = item.Value.GetProperty("type").GetString()!;
                            var declared = new RuleValueType(type == "choice" ? "text" : type, item.Value.TryGetProperty("unit", out JsonElement unit) ? unit.GetString() : null);
                            BindingReadModel.Require(!types.TryGetValue(item.Name, out RuleValueType previous) || previous == declared, "/spec", "configuration operand types are ambiguous");
                            types[item.Name] = declared;
                            BindingReadModel.Require(rule.Kind != KnowledgeKind.Workflow || !items.TryGetPropertyValue(item.Name, out JsonNode? existing) || JsonElement.DeepEquals(JsonSerializer.SerializeToElement(existing), item.Value), "/spec", "configuration definitions are ambiguous");
                            items[item.Name] = JsonNode.Parse(item.Value.GetRawText());
                        }
                    }

                    if (rule.Kind == KnowledgeKind.Workflow)
                    {
                        WorkflowValidator.Validate(JsonSerializer.SerializeToElement(rule.Spec, PackJson.Options), JsonSerializer.SerializeToElement(new { items }), (reference, kind) => resolve(reference, kind) is not null);
                    }
                    else
                    {
                        JsonElement spec = JsonSerializer.SerializeToElement(rule.Spec, PackJson.Options);
                        PackResource? ResolveResource(string reference) => Enum.GetValues<ResourceKind>()
                            .Where(kind => (AssetNames.TryParse(reference, out _, out _, out string? local) ? local : reference).StartsWith(JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString()) + "/", StringComparison.Ordinal))
                            .Select(kind => resolve(reference, kind)).FirstOrDefault();
                        _ = rule.Kind switch
                        {
                            KnowledgeKind.Module => (object)ModuleValidator.Module(spec, ResolveResource, name => types.TryGetValue(name, out RuleValueType type) ? type : null),
                            KnowledgeKind.MaterialMapping => ModuleValidator.Mapping(spec, ResolveResource),
                            KnowledgeKind.Sample => ModuleValidator.Sample(spec, ResolveResource),
                            _ => RuleValidator.Validate(spec, name => types.TryGetValue(name, out RuleValueType type) ? type : null),
                        };
                    }
                }
            }
            catch (PackFormatException ex)
            {
                return ex.Message;
            }
        }

        if (resource.Kind is ResourceKind.ConfigSchema or ResourceKind.ConfigLayer || AssetNames.KindOf(AssetNames.Local(resource)) is "config_schema" or "config_layer")
        {
            try
            {
                KnowledgeDocument document = KnowledgeDocument.Read(await ReadSmallAsync(fullPath, resource.Path, cancellationToken).ConfigureAwait(false));
                BindingReadModel.Require(KnowledgeDocument.ResourceKindOf(document.Kind) == resource.Kind, "/kind", "must match the resource kind");
                BindingReadModel.Require(AssetNames.Local(resource) == $"{KnowledgeDocument.Folder(document.Kind)}/{document.Metadata.Name}", "/metadata/name", "must match the resource id");
                JsonElement spec = JsonSerializer.SerializeToElement(document.Spec, PackJson.Options);
                if (resource.Kind == ResourceKind.ConfigSchema)
                {
                    ConfigValidator.Schema(spec);
                }
                else
                {
                    JsonElement? schema = null;
                    if (spec.TryGetProperty("schema", out JsonElement reference) && reference.ValueKind == JsonValueKind.String
                        && manifest.Resources.FirstOrDefault(r => r.Kind == ResourceKind.ConfigSchema && r.Storage == ResourceStorage.Bundled && AssetNames.Local(r) == reference.GetString()) is { } source
                        && present.TryGetValue(source.Path, out string? sourcePath))
                    {
                        KnowledgeDocument resolved = KnowledgeDocument.Read(await ReadSmallAsync(sourcePath, source.Path, cancellationToken).ConfigureAwait(false));
                        BindingReadModel.Require(resolved.Kind == KnowledgeKind.ConfigSchema, "/spec/schema", "must reference a config_schema document");
                        schema = JsonSerializer.SerializeToElement(resolved.Spec, PackJson.Options);
                    }

                    ConfigValidator.Layer(spec, _ => schema);
                }
            }
            catch (PackFormatException ex)
            {
                return ex.Message;
            }
        }

        return resource.Path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? ContentChecks.Svg(fullPath)
            : officialRule ? await ContentChecks.RuleSourceAsync(fullPath, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>The hash, size and file count of a resource's files; for a project relative to the link file's folder.</summary>
    private static async Task<(string Sha256, long Bytes, int Files)> MeasureAsync(PackResource resource, List<KeyValuePair<string, string>> own, CancellationToken cancellationToken)
    {
        try
        {
            if (resource.Kind != ResourceKind.Project)
            {
                (string sha256, long bytes) = await PackHasher.HashFileAsync(own[0].Value, cancellationToken).ConfigureAwait(false);
                return (sha256, bytes, 1);
            }

            int folder = resource.Path.LastIndexOf('/') + 1;
            return await PackHasher.HashTreeAsync(own.Select(o => (o.Key[folder..], o.Value)), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return ("cannot be read", -1, 0);
        }
    }

    /// <summary>A lock file in a project's data folder belongs to nothing, so a pack holding one fails the check.</summary>
    private static bool Belongs(PackResource resource, string relative) =>
        PackPath.Comparer.Equals(relative, resource.Path)
        || (resource.Kind == ResourceKind.Project && relative.StartsWith(resource.Path[..^4] + ".edb/", StringComparison.OrdinalIgnoreCase)
            && !PackHasher.IsLockFile(relative));

    /// <summary>
    /// The manifest of the pack in <paramref name="directory"/>, its pack digest and the problems of the manifest alone,
    /// without hashing any file: for lists and searches, which must stay fast; <see cref="CheckDirectoryAsync"/> checks
    /// the files. A manifest that cannot be read gives one problem and no manifest.
    /// </summary>
    public static async Task<(PackManifest? Manifest, string? Digest, IReadOnlyList<PackProblem> Problems)> ReadManifestAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await ReadSmallAsync(Path.Combine(directory, PackManifest.FileName), PackManifest.FileName, cancellationToken).ConfigureAwait(false);
            PackManifest manifest = PackJson.ReadManifest(bytes);
            return (manifest, PackDigest.Compute(bytes), PackManifestValidator.Validate(manifest));
        }
        catch (PackFormatException ex)
        {
            return (null, null, [new PackProblem(PackManifest.FileName, ex.Message)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return (null, null, [new PackProblem(PackManifest.FileName, "cannot be read")]);
        }
    }

    /// <summary>
    /// What makes the referenced <paramref name="resource"/> unavailable now, or null (ADR-0024 section 6): its root is
    /// not among <paramref name="roots"/> (config.json packSources, name to path), the path below it is missing or leads
    /// through a link, or its files no longer have the hash the manifest records. Reads only.
    /// </summary>
    public static async Task<string?> ReferenceProblemAsync(PackResource resource, IReadOnlyDictionary<string, string> roots, CancellationToken cancellationToken)
    {
        if (resource.Reference is not { } reference || !roots.TryGetValue(reference.Root, out string? root))
        {
            return $"is referenced below '{resource.Reference?.Root}', which config.json packSources does not list";
        }

        string current = root;
        foreach (string step in reference.Path.Split('/'))
        {
            current = Path.Combine(current, step);
            var entry = new FileInfo(current);
            if (!entry.Exists && !Directory.Exists(current))
            {
                return $"is missing below the pack source '{reference.Root}'";
            }

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return "is reached through a symbolic link or junction, which packs never follow";
            }
        }

        try
        {
            (string sha256, long bytes, int files) = resource.Kind == ResourceKind.Project
                ? await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(current), cancellationToken).ConfigureAwait(false)
                : await HashOneAsync(current, cancellationToken).ConfigureAwait(false);
            return (sha256, bytes, files) == (resource.Sha256, resource.Bytes, resource.Files)
                ? null
                : "has changed below its pack source since it was imported (its SHA-256 differs), so it is not used";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or PackFormatException)
        {
            return "cannot be read below its pack source";
        }
    }

    /// <summary>
    /// What makes the one resource <paramref name="resource"/> of the pack in <paramref name="folder"/> unavailable now,
    /// or null: a bundled resource must have the hash, size and file count pack.json records; a referenced one is
    /// checked below its root (<see cref="ReferenceProblemAsync"/>). Cheaper than checking the whole pack.
    /// </summary>
    public static async Task<string?> ResourceProblemAsync(string folder, PackResource resource, IReadOnlyDictionary<string, string> roots, CancellationToken cancellationToken)
    {
        if (resource.Storage == ResourceStorage.Referenced)
        {
            return await ReferenceProblemAsync(resource, roots, cancellationToken).ConfigureAwait(false);
        }

        string full = Path.Combine(folder, resource.Path.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            (string sha256, long bytes, int files) = !File.Exists(full) ? (string.Empty, -1, 0)
                : resource.Kind == ResourceKind.Project ? await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(full), cancellationToken).ConfigureAwait(false)
                : await HashOneAsync(full, cancellationToken).ConfigureAwait(false);
            return bytes < 0 ? "is missing from the pack"
                : (sha256, bytes, files) == (resource.Sha256, resource.Bytes, resource.Files) ? null
                : "does not match pack.json: its files changed after the manifest was written";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or PackFormatException)
        {
            return "cannot be read";
        }
    }

    private static async Task<(string, long, int)> HashOneAsync(string path, CancellationToken cancellationToken)
    {
        (string sha256, long bytes) = await PackHasher.HashFileAsync(path, cancellationToken).ConfigureAwait(false);
        return (sha256, bytes, 1);
    }

    internal static async Task<byte[]> ReadSmallAsync(string path, string name, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > PackJson.MaxFileBytes)
        {
            throw new PackFormatException($"{name} is longer than {PackJson.MaxFileBytes / (1024 * 1024)} MB.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
