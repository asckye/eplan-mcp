namespace EplanMcp.Packs.Scanning;

/// <summary>
/// Writes a scanned pack as a folder: copies every bundled resource's files unchanged (referenced ones stay where they
/// are), writes <c>pack.json</c> and, for a pack without dependencies, <c>pack.lock.json</c>. Writes into a new folder beside the target and gives it the target's name
/// only once the folder checks clean, so a file that changed after the scan never ends up in a pack. Refuses while the
/// scan has errors, and never writes into an existing folder.
/// </summary>
public static class PackWriter
{
    public static async Task<PackCheckResult> WriteAsync(ScanResult scan, PackManifest manifest, string directory, CancellationToken cancellationToken)
    {
        if (scan.HasErrors)
        {
            throw new PackFormatException("The inputs have errors; fix them and scan again before writing a pack.");
        }

        if (PackManifestValidator.Validate(manifest) is [var first, ..])
        {
            throw new PackFormatException($"The manifest is not valid: {first}");
        }

        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new PackFormatException($"'{Path.GetFileName(target)}' exists already; a pack is written into a new folder.");
        }

        string staging = $"{target}.partial-{Guid.NewGuid():N}";
        try
        {
            foreach (ScannedResource resource in scan.Resources.Where(r => r.Resource.Storage == ResourceStorage.Bundled))
            {
                string folder = Path.Combine(staging, Path.GetDirectoryName(resource.Resource.Path.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty);
                foreach ((string relative, string fullPath) in resource.FilesOnDisk)
                {
                    await CopyAsync(fullPath, Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)), cancellationToken).ConfigureAwait(false);
                }
            }

            byte[] json = PackJson.Serialize(manifest);
            await File.WriteAllBytesAsync(Path.Combine(staging, PackManifest.FileName), json, cancellationToken).ConfigureAwait(false);
            if (manifest.Dependencies.Count == 0)
            {
                var self = new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(json) };
                await File.WriteAllBytesAsync(Path.Combine(staging, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(self, [])), cancellationToken).ConfigureAwait(false);
            }

            PackCheckResult check = await PackVerifier.CheckDirectoryAsync(staging, cancellationToken).ConfigureAwait(false);
            if (check.Ok)
            {
                Directory.Move(staging, target);
            }

            return check;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>Copies one file into a new file, creating its folder; never overwrites.</summary>
    internal static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (input.ConfigureAwait(false))
        {
            var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await using (output.ConfigureAwait(false))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
