using System.Security;

namespace EplanMcp.Packs;

/// <summary>
/// Checks a pack in its directory form: <c>pack.json</c> reads and validates, every bundled resource is there with the
/// hash, size and file count the manifest records, the lock (when present) fits the manifest, and nothing else is in the
/// folder. A symbolic link or junction anywhere in the pack fails the check. Reads only.
/// </summary>
public static class PackVerifier
{
    public static async Task<PackCheckResult> CheckDirectoryAsync(string directory, CancellationToken cancellationToken)
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
                : await CheckContentAsync(manifest, PackDigest.Compute(manifestBytes), present, cancellationToken).ConfigureAwait(false);
        }
        catch (PackFormatException ex)
        {
            return PackCheckResult.Failed(PackManifest.FileName, ex.Message);
        }
    }

    private static async Task<PackCheckResult> CheckContentAsync(PackManifest manifest, string manifestSha256, Dictionary<string, string> present, CancellationToken cancellationToken)
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

        foreach (PackResource resource in manifest.Resources)
        {
            List<KeyValuePair<string, string>> own = [.. present.Where(p => Belongs(resource, p.Key))];
            listed.UnionWith(own.Select(o => o.Key));
            if (resource.Storage == ResourceStorage.Referenced)
            {
                if (own.Count > 0)
                {
                    problems.Add(new PackProblem(resource.Path, "is referenced, but its files are in the pack"));
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
        }

        problems.AddRange(present.Keys.Where(k => !listed.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => new PackProblem(k, "is in the pack but not in pack.json")));
        return new PackCheckResult(manifest, lockFile, problems);
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

    internal static async Task<byte[]> ReadSmallAsync(string path, string name, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length > PackJson.MaxFileBytes)
        {
            throw new PackFormatException($"{name} is longer than {PackJson.MaxFileBytes / (1024 * 1024)} MB.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
