using System.Diagnostics;

namespace EplanMcp.Packs;

/// <summary>
/// The installed packs, <c>&lt;data root&gt;\packs\</c>: each version in its own folder <c>&lt;id&gt;\&lt;version&gt;\</c>, its
/// files marked read-only. An installed version never changes: installing it again with the same <c>pack.json</c> does
/// nothing, with another <c>pack.json</c> is refused; a newer version goes into its own folder and changes no binding.
/// <c>bindings.json</c> pins each target to exact versions by their pack digests. Whatever changes the store (moving a checked version into
/// place, writing the bindings) holds the exclusive lock file <c>.store.lock</c>; checking and extracting a pack happen
/// before, in a staging folder of its own, so a long extraction does not keep others waiting.
/// </summary>
public sealed class PackStore(string root)
{
    public const string LockFileName = ".store.lock";

    private const string Busy = "is being changed by another EplanMcp (an install or a binding); try again when it is done";

    public string Root { get; } = root;

    /// <summary>How long a change waits for another one to release the store's lock before it gives up.</summary>
    public TimeSpan LockWait { get; init; } = TimeSpan.FromSeconds(30);

    public string FolderOf(string id, string version) => Path.Combine(Root, id, version);

    /// <summary>Installs the pack folder or <c>.eplanpack</c> file <paramref name="source"/> after checking it completely.</summary>
    public async Task<PackInstallResult> InstallAsync(string source, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        string staging = Path.Combine(Root, $".staging-{Guid.NewGuid():N}");
        try
        {
            PackCheckResult check = File.Exists(source)
                ? await PackArchive.ExtractAsync(source, staging, PackLimits.Default, cancellationToken).ConfigureAwait(false)
                : await CopyCheckedAsync(source, staging, cancellationToken).ConfigureAwait(false);
            if (!check.Ok || check.Manifest is not { } manifest)
            {
                return new PackInstallResult(PackInstallOutcome.Refused, check.Manifest, check.Problems);
            }

            using FileStream? held = await LockAsync(cancellationToken).ConfigureAwait(false);
            if (held is null)
            {
                return new PackInstallResult(PackInstallOutcome.Refused, manifest, [new PackProblem("pack store", Busy)]);
            }

            string target = FolderOf(manifest.Id, manifest.Version);
            if (!Directory.Exists(target))
            {
                foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.ReadOnly);
                }

                Directory.CreateDirectory(Path.Combine(Root, manifest.Id));
                Directory.Move(staging, target);
                return new PackInstallResult(PackInstallOutcome.Installed, manifest, []);
            }

            return DigestOf(target) == DigestOf(staging)
                ? new PackInstallResult(PackInstallOutcome.AlreadyInstalled, manifest, [])
                : new PackInstallResult(PackInstallOutcome.Refused, manifest,
                    [new PackProblem($"{manifest.Id} {manifest.Version}", "is installed already with other content; a changed pack needs a new version")]);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>The installed versions of <paramref name="id"/>, highest first.</summary>
    public IReadOnlyList<SemanticVersion> Versions(string id)
    {
        var folder = new DirectoryInfo(Path.Combine(Root, id));
        return !PackManifestValidator.IsId(id) || !folder.Exists ? [] :
        [
            .. folder.EnumerateDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, PackManifest.FileName)))
                .Select(d => SemanticVersion.TryParse(d.Name, out SemanticVersion? version) ? version : null)
                .OfType<SemanticVersion>()
                .OrderDescending(),
        ];
    }

    /// <summary>
    /// The lock of <paramref name="manifest"/> against the installed packs: for each dependency, directly or through
    /// another pack, the highest installed version in range, one version per id. No backtracking: a conflict is a problem.
    /// </summary>
    public (PackLock? Lock, IReadOnlyList<PackProblem> Problems) Lock(PackManifest manifest, string manifestDigest)
    {
        var chosen = new Dictionary<string, PackReference>(StringComparer.Ordinal);
        var problems = new List<PackProblem>();
        var pending = new Queue<(PackDependency Dependency, string From)>(manifest.Dependencies.Select(d => (d, manifest.Id)));
        while (pending.TryDequeue(out (PackDependency Dependency, string From) next))
        {
            (PackDependency dependency, string from) = next;
            if (!VersionRange.TryParse(dependency.Version, out VersionRange? range))
            {
                problems.Add(new PackProblem(dependency.Id, $"'{from}' gives the range '{dependency.Version}', which does not parse"));
            }
            else if (dependency.Id == manifest.Id)
            {
                problems.Add(new PackProblem(dependency.Id, $"'{from}' needs the pack being locked; dependencies must not form a cycle"));
            }
            else if (chosen.TryGetValue(dependency.Id, out PackReference? taken))
            {
                if (!range.Contains(SemanticVersion.Parse(taken.Version)))
                {
                    problems.Add(new PackProblem(dependency.Id, $"'{from}' needs '{dependency.Version}', but {taken.Version} is chosen for another pack"));
                }
            }
            else if (Versions(dependency.Id).FirstOrDefault(range.Contains) is not { } best)
            {
                problems.Add(new PackProblem(dependency.Id, $"no installed version fits '{dependency.Version}', which '{from}' needs; install one first"));
            }
            else
            {
                string folder = FolderOf(dependency.Id, best.ToString());
                chosen[dependency.Id] = new PackReference { Id = dependency.Id, Version = best.ToString(), Digest = DigestOf(folder) };
                foreach (PackDependency further in PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(folder, PackManifest.FileName))).Dependencies)
                {
                    pending.Enqueue((further, dependency.Id));
                }
            }
        }

        var self = new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = manifestDigest };
        return problems.Count > 0 ? (null, problems) : (PackLock.Create(self, chosen.Values), []);
    }

    /// <summary>Pins <paramref name="target"/> to the installed version <paramref name="version"/> of <paramref name="id"/>, replacing its pin of that id.</summary>
    public async Task<PackReference> BindAsync(string target, string id, string version, CancellationToken cancellationToken)
    {
        string folder = FolderOf(id, version);
        if (!PackManifestValidator.IsId(id) || !SemanticVersion.TryParse(version, out _) || !File.Exists(Path.Combine(folder, PackManifest.FileName)))
        {
            throw new PackFormatException($"{id} {version} is not installed; install it before binding it.");
        }

        var pin = new PackReference { Id = id, Version = version, Digest = DigestOf(folder) };
        using FileStream held = await LockAsync(cancellationToken).ConfigureAwait(false) ?? throw new PackFormatException("The pack store " + Busy + ".");
        PackBindings bindings = await ReadBindingsAsync(cancellationToken).ConfigureAwait(false);
        PackBinding binding = bindings.Bindings.FirstOrDefault(b => b.Target == target) ?? new PackBinding { Target = target };
        binding = binding with { Packs = [.. binding.Packs.Where(p => p.Id != id).Append(pin).OrderBy(p => p.Id, StringComparer.Ordinal)] };
        bindings = bindings with { Bindings = [.. bindings.Bindings.Where(b => b.Target != target).Append(binding).OrderBy(b => b.Target, StringComparer.Ordinal)] };
        string file = Path.Combine(Root, PackBindings.FileName);
        string partial = $"{file}.partial-{Guid.NewGuid():N}";
        Directory.CreateDirectory(Root);
        await File.WriteAllBytesAsync(partial, PackJson.Serialize(bindings), cancellationToken).ConfigureAwait(false);
        File.Move(partial, file, overwrite: true);
        return pin;
    }

    /// <summary>The versions <paramref name="target"/> is pinned to, and a problem for each pin whose folder is gone or changed.</summary>
    public async Task<(IReadOnlyList<PackReference> Packs, IReadOnlyList<PackProblem> Problems)> BoundAsync(string target, CancellationToken cancellationToken)
    {
        PackBindings bindings = await ReadBindingsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PackReference> packs = bindings.Bindings.FirstOrDefault(b => b.Target == target)?.Packs ?? [];
        PackProblem[] problems =
        [
            .. packs.Where(p => !File.Exists(Path.Combine(FolderOf(p.Id, p.Version), PackManifest.FileName)) || DigestOf(FolderOf(p.Id, p.Version)) != p.Digest)
                .Select(p => new PackProblem($"{p.Id} {p.Version}", "is pinned but no longer installed as it was")),
        ];
        return (packs, problems);
    }

    /// <summary>The store's lock, or null when another holder keeps it longer than <see cref="LockWait"/>.</summary>
    private async Task<FileStream?> LockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(Path.Combine(Root, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < LockWait)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private async Task<PackBindings> ReadBindingsAsync(CancellationToken cancellationToken)
    {
        string file = Path.Combine(Root, PackBindings.FileName);
        return File.Exists(file)
            ? PackJson.Read<PackBindings>(await PackVerifier.ReadSmallAsync(file, PackBindings.FileName, cancellationToken).ConfigureAwait(false), PackBindings.FileName, "bindingsVersion", PackBindings.CurrentBindingsVersion)
            : new PackBindings { BindingsVersion = PackBindings.CurrentBindingsVersion };
    }

    private static async Task<PackCheckResult> CopyCheckedAsync(string source, string staging, CancellationToken cancellationToken)
    {
        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(source, cancellationToken).ConfigureAwait(false);
        if (!check.Ok)
        {
            return check;
        }

        var files = new List<(string Relative, string FullPath)>();
        PackHasher.Collect(new DirectoryInfo(source), string.Empty, files);
        foreach ((string relative, string fullPath) in files)
        {
            string destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(fullPath, destination);
            File.SetAttributes(destination, FileAttributes.Normal);
        }

        // Checked again in place: the source may have changed while it was copied.
        return await PackVerifier.CheckDirectoryAsync(staging, cancellationToken).ConfigureAwait(false);
    }

    private static string DigestOf(string folder) => PackDigest.Compute(File.ReadAllBytes(Path.Combine(folder, PackManifest.FileName)));
}
