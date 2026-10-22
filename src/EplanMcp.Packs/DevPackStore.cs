using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs;

/// <summary>
/// The development packs, <c>&lt;data root&gt;\packs-dev\&lt;id&gt;\</c> (M6 design 4.1.1): plain pack folders that only
/// EplanMcp's tools write; an engineer may edit one by hand, and the next change checks it first. A change holds the
/// lock file <c>.&lt;id&gt;.lock</c> beside the folder, assembles the new pack in a staging folder beside it (the current
/// files, the new ones, <c>pack.json</c>, <c>pack.lock.json</c>), checks it completely, and only then puts it in place
/// of the old folder: a pack that fails its check is never changed, and a file that changed after its scan never gets
/// in. A development pack's manifest has the version <see cref="ManifestVersion"/>; building gives it a real one.
/// </summary>
public sealed class DevPackStore(string root)
{
    /// <summary>The version in every development pack's manifest; asset ids show it as <c>dev</c>.</summary>
    public const string ManifestVersion = "0.0.0-dev";

    public string Root { get; } = root;

    /// <summary>How long a change waits for another one to release the pack's lock before it gives up.</summary>
    public TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(30);

    public string FolderOf(string id) => Path.Combine(Root, id);

    /// <summary>The development packs' ids, sorted; folders not named like a pack, links and folders without pack.json are left out.</summary>
    public IReadOnlyList<string> Ids() => !Directory.Exists(Root) ? [] :
    [
        .. new DirectoryInfo(Root).EnumerateDirectories()
            .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0 && PackManifestValidator.IsId(d.Name) && File.Exists(Path.Combine(d.FullName, PackManifest.FileName)))
            .Select(d => d.Name)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// Adds the scanned resources to pack <paramref name="id"/>, created with <paramref name="name"/> when missing: a
    /// resource already there with the same hash stays, one with another hash is replaced only with
    /// <paramref name="replace"/>, and every gap the scan found becomes a gap document. Rules, mappings and documents
    /// that are new or replaced are candidates. Throws <see cref="DevPackException"/>.
    /// </summary>
    public async Task<DevPackChange> ImportAsync(string id, string name, ScanResult scan, bool replace, string createdBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (scan.HasErrors)
        {
            throw new PackFormatException("The inputs have errors; fix them before importing.");
        }

        List<PackResource> added = [], replaced = [];
        List<string> unchanged = [], gaps = [];
        (PackManifest manifest, string digest, bool created) = await ChangeAsync(id, name, createdBy, now, async (current, staging) =>
        {
            Dictionary<string, PackResource> resources = current.Resources.ToDictionary(r => r.Path, PackPath.Comparer);
            List<PackFact> facts = [.. current.Facts];
            List<PackProblem> conflicts = [];
            foreach (ScannedResource scanned in scan.Resources)
            {
                PackResource resource = scanned.Resource;
                if (resources.TryGetValue(resource.Path, out PackResource? existing))
                {
                    if (existing.Sha256 == resource.Sha256 && existing.Storage == resource.Storage)
                    {
                        unchanged.Add(resource.Path);
                        continue;
                    }

                    if (!replace)
                    {
                        conflicts.Add(new PackProblem(resource.Path, "is in the pack already, with other content"));
                        continue;
                    }

                    DeleteFiles(staging, existing);
                    facts.RemoveAll(f => PackPath.Comparer.Equals(f.Resource, existing.Path));
                    replaced.Add(resource);
                }
                else
                {
                    added.Add(resource);
                }

                if (resource.Storage == ResourceStorage.Bundled)
                {
                    await CopyAsync(scanned, staging, cancellationToken).ConfigureAwait(false);
                }

                resources[resource.Path] = resource;
                facts.AddRange(scanned.Facts);
            }

            if (conflicts.Count > 0)
            {
                throw new DevPackException(DevPackError.Conflict, conflicts);
            }

            HashSet<string> changed = new(added.Concat(replaced).Select(r => r.Path), PackPath.Comparer);
            if (changed.Count == 0 && current.Resources.Count > 0)
            {
                return null;
            }

            foreach (KnowledgeDocument gap in GapsOf(scan, changed))
            {
                PackResource written = await WriteDocumentAsync(staging, gap, cancellationToken).ConfigureAwait(false);
                resources[written.Path] = written;
                gaps.Add(written.Path);
            }

            return Assemble(current, resources.Values, facts, scan.Inputs, changed);
        }, cancellationToken).ConfigureAwait(false);
        return new DevPackChange(manifest, digest, created, added, replaced, unchanged, gaps);
    }

    /// <summary>
    /// The change itself: under the pack's lock, <paramref name="build"/> gets the current manifest (a new empty one for
    /// a new pack) and the staging folder holding the current files, writes its files there and returns the new
    /// manifest, or null when nothing changes, which leaves the pack as it is; then pack.json and pack.lock.json are
    /// written, the staging folder is checked and put in place.
    /// </summary>
    private async Task<(PackManifest Manifest, string Digest, bool Created)> ChangeAsync(
        string id, string name, string createdBy, DateTimeOffset now, Func<PackManifest, string, Task<PackManifest?>> build, CancellationToken cancellationToken)
    {
        if (!PackManifestValidator.IsId(id))
        {
            throw new PackFormatException($"'{id}' is not a pack id: lowercase letters and digits joined by '.' or '-', such as acme.panels.");
        }

        Directory.CreateDirectory(Root);
        using FileStream held = await FileLock.AcquireAsync(Path.Combine(Root, $".{id}.lock"), LockWait, cancellationToken).ConfigureAwait(false)
            ?? throw new DevPackException(DevPackError.Busy, [new PackProblem(id, "is being changed by another EplanMcp; try again when it is done")]);
        string folder = FolderOf(id);
        PackManifest? current = null;
        if (Directory.Exists(folder))
        {
            PackCheckResult check = await PackVerifier.CheckDirectoryAsync(folder, cancellationToken).ConfigureAwait(false);
            current = check.Ok ? check.Manifest : throw new DevPackException(DevPackError.Invalid, check.Problems);
        }

        string staging = Path.Combine(Root, $".{id}.staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            if (current is not null)
            {
                CopyTree(folder, staging);
            }

            if (await build(current ?? Fresh(id, name, createdBy, now), staging).ConfigureAwait(false) is not { } next)
            {
                return (current!, PackDigest.Compute(await File.ReadAllBytesAsync(Path.Combine(folder, PackManifest.FileName), cancellationToken).ConfigureAwait(false)), false);
            }

            next = next with { Provenance = next.Provenance with { CreatedBy = createdBy, CreatedAt = now.ToUniversalTime() } };
            if (PackManifestValidator.Validate(next) is { Count: > 0 } invalid)
            {
                throw new DevPackException(DevPackError.Invalid, invalid);
            }

            byte[] json = PackJson.Serialize(next);
            string digest = PackDigest.Compute(json);
            await File.WriteAllBytesAsync(Path.Combine(staging, PackManifest.FileName), json, cancellationToken).ConfigureAwait(false);
            string lockFile = Path.Combine(staging, PackManifest.LockFileName);
            File.Delete(lockFile);
            if (next.Dependencies.Count == 0)
            {
                var self = new PackReference { Id = next.Id, Version = next.Version, Digest = digest };
                await File.WriteAllBytesAsync(lockFile, PackJson.Serialize(PackLock.Create(self, [])), cancellationToken).ConfigureAwait(false);
            }

            PackCheckResult result = await PackVerifier.CheckDirectoryAsync(staging, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                throw new DevPackException(DevPackError.Invalid, result.Problems);
            }

            Replace(folder, staging);
            return (next, digest, current is null);
        }
        finally
        {
            DeleteTree(staging);
        }
    }

    private static PackManifest Fresh(string id, string name, string createdBy, DateTimeOffset now) => new()
    {
        SchemaVersion = PackManifest.CurrentSchemaVersion,
        Id = id,
        Name = name,
        Version = ManifestVersion,
        Compatibility = new PackCompatibility(),
        Resources = [],
        Provenance = new PackProvenance { CreatedBy = createdBy, CreatedAt = now.ToUniversalTime() },
    };

    /// <summary>The manifest with these resources and facts in canonical order; slots of new or replaced resources are candidates.</summary>
    private static PackManifest Assemble(PackManifest current, IEnumerable<PackResource> resources, IEnumerable<PackFact> facts, IEnumerable<PackInput> inputs, HashSet<string> changed)
    {
        List<PackResource> sorted = [.. resources.OrderBy(r => r.Path, StringComparer.Ordinal)];
        HashSet<string> paths = new(sorted.Select(r => r.Path), PackPath.Comparer);
        List<PackSlot> Slots(IReadOnlyList<PackSlot> slots, ResourceKind kind) =>
        [
            .. sorted.Where(r => r.Kind == kind).Select(r => (changed.Contains(r.Path) ? null : slots.FirstOrDefault(s => PackPath.Comparer.Equals(s.Path, r.Path)))
                ?? new PackSlot { Path = r.Path, Standing = KnowledgeStanding.Candidate }),
        ];
        return current with
        {
            Resources = sorted,
            Rules = Slots(current.Rules, ResourceKind.Rule),
            Mappings = Slots(current.Mappings, ResourceKind.MaterialMapping),
            Docs = Slots(current.Docs, ResourceKind.Document),
            AcceptanceSamples = [.. current.AcceptanceSamples.Where(s => paths.Contains(s.Path) && !changed.Contains(s.Path))],
            Facts = [.. facts.Where(f => paths.Contains(f.Resource)).OrderBy(f => f.Resource, StringComparer.Ordinal).ThenBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Evidence, StringComparer.Ordinal)],
            Provenance = current.Provenance with { Inputs = [.. current.Provenance.Inputs.Concat(inputs).DistinctBy(i => i.Name, StringComparer.Ordinal)] },
        };
    }

    /// <summary>A gap document for each gap the scan found in a new or replaced resource, named by its code and the resource's path.</summary>
    private static IEnumerable<KnowledgeDocument> GapsOf(ScanResult scan, HashSet<string> changed)
    {
        foreach (ScannedResource scanned in scan.Resources.Where(r => changed.Contains(r.Resource.Path)))
        {
            string subject = scanned.Resource.Source is { } source ? source.Path.Length == 0 ? source.Input : source.Input + "/" + source.Path : string.Empty;
            foreach (ScanFinding finding in scan.Findings.Where(f => f.Severity == FindingSeverity.Gap && f.Subject == subject))
            {
                string code = finding.Code.Replace('_', '-');
                string local = AssetNames.Local(scanned.Resource);
                yield return new KnowledgeDocument
                {
                    ApiVersion = KnowledgeDocument.CurrentApiVersion,
                    Kind = KnowledgeKind.Gap,
                    Metadata = new KnowledgeMetadata
                    {
                        Name = $"{code}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scanned.Resource.Path)))[..8]}",
                        Tags = ["import", code],
                    },
                    Origin = new KnowledgeOrigin { Asset = local, Note = "found by EplanMcp's import" },
                    Spec = new JsonObject { ["missing"] = finding.Message, ["related"] = new JsonArray(local), ["status"] = "open" },
                };
            }
        }
    }

    /// <summary>Writes <paramref name="document"/> at its path below <paramref name="staging"/>, replacing one there, and returns its resource.</summary>
    internal static async Task<PackResource> WriteDocumentAsync(string staging, KnowledgeDocument document, CancellationToken cancellationToken)
    {
        string path = KnowledgeDocument.PathOf(document.Kind, document.Metadata.Name);
        string full = Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        byte[] bytes = PackJson.Serialize(document);
        await File.WriteAllBytesAsync(full, bytes, cancellationToken).ConfigureAwait(false);
        return new PackResource
        {
            Path = path,
            Kind = KnowledgeDocument.ResourceKindOf(document.Kind),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Bytes = bytes.Length,
            Files = 1,
            Storage = ResourceStorage.Bundled,
        };
    }

    private static async Task CopyAsync(ScannedResource scanned, string staging, CancellationToken cancellationToken)
    {
        string folder = Path.Combine(staging, Path.GetDirectoryName(scanned.Resource.Path.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty);
        foreach ((string relative, string fullPath) in scanned.FilesOnDisk)
        {
            await PackWriter.CopyAsync(fullPath, Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A resource's files in <paramref name="staging"/>: the file, or a project's link file and data folder.</summary>
    private static void DeleteFiles(string staging, PackResource resource)
    {
        string full = Path.Combine(staging, resource.Path.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(full);
        if (resource.Kind == ResourceKind.Project)
        {
            DeleteTree(Path.ChangeExtension(full, ".edb"));
        }
    }

    /// <summary>Copies every file of <paramref name="source"/> (never through a link) into <paramref name="destination"/>, writable.</summary>
    private static void CopyTree(string source, string destination)
    {
        var files = new List<(string Relative, string FullPath)>();
        PackHasher.Collect(new DirectoryInfo(source), string.Empty, files);
        foreach ((string relative, string fullPath) in files)
        {
            string target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(fullPath, target);
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    /// <summary>Puts <paramref name="staging"/> in place of <paramref name="folder"/>; the old folder is moved back if that fails.</summary>
    private void Replace(string folder, string staging)
    {
        if (!Directory.Exists(folder))
        {
            Directory.Move(staging, folder);
            return;
        }

        string old = Path.Combine(Root, $".{Path.GetFileName(folder)}.old-{Guid.NewGuid():N}");
        Directory.Move(folder, old);
        try
        {
            Directory.Move(staging, folder);
        }
        catch (IOException)
        {
            Directory.Move(old, folder);
            throw;
        }

        DeleteTree(old);
    }

    /// <summary>Deletes a folder this store made, read-only marks included; one that cannot be deleted now is left behind.</summary>
    private static void DeleteTree(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another program holds a file; the leftover folder starts with '.', so it is never taken for a pack.
        }
    }
}
