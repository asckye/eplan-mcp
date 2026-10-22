using System.IO.Compression;
using System.Text;

namespace EplanMcp.Packs;

/// <summary>
/// The <c>.eplanpack</c> form of a pack: a ZIP of the pack folder, files stored byte for byte. Export gives the same bytes
/// for the same folder (entries sorted, one fixed time, no attributes) and never overwrites a file. Extraction refuses
/// any entry that could land outside the target or be ambiguous on Windows, bounds the sizes, writes into a new folder
/// beside the target, checks it like a pack folder (every hash) and only then gives it the target's name.
/// </summary>
public static class PackArchive
{
    public const string Extension = ".eplanpack";

    /// <summary>The earliest time a ZIP entry can hold, so the output does not depend on when it was made.</summary>
    private static readonly DateTimeOffset _entryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Files that are compressed already (EPLAN's backups and basic projects are 7z): deflating them gains nothing.</summary>
    private static readonly string[] _compressed = [".zw1", ".zw6", ".zw7", ".zw9", ".zip", ".7z", ".xlsx", ".docx", ".pdf", ".png", ".jpg"];

    public static async Task ExportAsync(string directory, string file, CancellationToken cancellationToken)
    {
        if (!file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new PackFormatException($"The file name must end with {Extension}.");
        }

        if (File.Exists(file) || Directory.Exists(file))
        {
            throw new PackFormatException($"'{Path.GetFileName(file)}' exists already; EplanMcp never overwrites a file.");
        }

        PackCheckResult check = await PackVerifier.CheckDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        if (!check.Ok)
        {
            throw new PackFormatException($"The pack has {check.Problems.Count} problem(s), the first: {check.Problems[0]}");
        }

        var files = new List<(string Relative, string FullPath)>();
        PackHasher.Collect(new DirectoryInfo(directory), string.Empty, files);
        string partial = $"{file}.partial-{Guid.NewGuid():N}";
        try
        {
            var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
                foreach ((string relative, string fullPath) in files.OrderBy(f => f.Relative, StringComparer.Ordinal))
                {
                    bool compressed = _compressed.Any(e => relative.EndsWith(e, StringComparison.OrdinalIgnoreCase));
                    ZipArchiveEntry entry = zip.CreateEntry(relative, compressed ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    entry.LastWriteTime = _entryTime;
                    entry.ExternalAttributes = 0;
                    var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    await using (source.ConfigureAwait(false))
                    {
                        Stream target = entry.Open();
                        await using (target.ConfigureAwait(false))
                        {
                            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            File.Move(partial, file, overwrite: false);
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>
    /// What makes the entries of <paramref name="archive"/> unsafe to extract, reading only the ZIP directory: names that
    /// are not pack paths (absolute, <c>..</c>, <c>\</c>, drive, reserved), links, encrypted entries, duplicates and names
    /// that differ only in case, a name that is both a file and a folder, and sizes over <paramref name="limits"/>.
    /// </summary>
    public static IReadOnlyList<PackProblem> CheckEntries(ZipArchive archive, PackLimits limits)
    {
        if (archive.Entries.Count > limits.MaxEntries)
        {
            return [new PackProblem("archive", $"has {archive.Entries.Count} entries; at most {limits.MaxEntries} are allowed")];
        }

        var problems = new List<PackProblem>();
        var names = new Dictionary<string, string>(PackPath.Comparer);
        var files = new List<string>();
        var folders = new HashSet<string>(PackPath.Comparer);
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            bool folder = entry.FullName.EndsWith('/');
            string path = folder ? entry.FullName[..^1] : entry.FullName;
            string? problem = PackPath.Problem(path)
                ?? (IsLink(entry) ? "is a symbolic link or reparse point" : null)
                ?? (entry.IsEncrypted ? "is encrypted" : null)
                ?? (folder && entry.Length != 0 ? "is a folder entry with content" : null)
                ?? (entry.Length > limits.MaxEntryBytes ? $"is larger than {limits.MaxEntryBytes} bytes" : null)
                ?? (names.TryGetValue(path, out string? earlier)
                    ? earlier == path ? "is in the archive twice" : $"names the same file as '{earlier}' on Windows (only the case differs)"
                    : null);
            if (problem is not null)
            {
                problems.Add(new PackProblem(entry.FullName, problem));
                continue;
            }

            names.Add(path, path);
            if (folder)
            {
                folders.Add(path);
                continue;
            }

            files.Add(path);
            total += entry.Length;
            for (int slash = path.IndexOf('/', StringComparison.Ordinal); slash > 0; slash = path.IndexOf('/', slash + 1))
            {
                folders.Add(path[..slash]);
            }
        }

        problems.AddRange(files.Where(folders.Contains).Select(f => new PackProblem(f, "is both a file and a folder")));
        if (total > limits.MaxTotalBytes)
        {
            problems.Add(new PackProblem("archive", $"holds {total} bytes; at most {limits.MaxTotalBytes} are allowed"));
        }

        return problems;
    }

    /// <summary>
    /// Extracts <paramref name="file"/> into the new folder <paramref name="destination"/> once every entry passes
    /// <see cref="CheckEntries"/> and its <c>pack.json</c> reads; the folder exists afterwards only when the pack checks
    /// clean. Never writes outside a folder it created.
    /// </summary>
    public static async Task<PackCheckResult> ExtractAsync(string file, string destination, PackLimits limits, CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(file);
        if (!File.Exists(file))
        {
            return PackCheckResult.Failed(name, "does not exist");
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            return PackCheckResult.Failed("destination", "exists already; a pack is extracted into a new folder");
        }

        string staging = $"{Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination))}.partial-{Guid.NewGuid():N}";
        try
        {
            using (ZipArchive archive = Open(file))
            {
                IReadOnlyList<PackProblem> problems = CheckEntries(archive, limits);
                if (problems.Count > 0)
                {
                    return new PackCheckResult(null, null, problems);
                }

                // The manifest first: a pack in a format this EplanMcp cannot read is refused before anything is written.
                if (archive.GetEntry(PackManifest.FileName) is not { Length: <= PackJson.MaxFileBytes } manifest)
                {
                    return PackCheckResult.Failed(PackManifest.FileName, "is missing (or too long): the file is not a pack");
                }

                using (var bytes = new MemoryStream())
                {
                    await CopyBoundedAsync(manifest, bytes, cancellationToken).ConfigureAwait(false);
                    PackJson.ReadManifest(bytes.ToArray());
                }

                Directory.CreateDirectory(staging);
                foreach (ZipArchiveEntry entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
                {
                    string target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return PackCheckResult.Failed(entry.FullName, "would be written outside the pack");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                    await using (output.ConfigureAwait(false))
                    {
                        await CopyBoundedAsync(entry, output, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            PackCheckResult check = await PackVerifier.CheckDirectoryAsync(staging, cancellationToken).ConfigureAwait(false);
            if (check.Ok)
            {
                Directory.Move(staging, destination);
            }

            return check;
        }
        catch (PackFormatException ex)
        {
            return PackCheckResult.Failed(name, ex.Message);
        }
        catch (InvalidDataException)
        {
            return PackCheckResult.Failed(name, "is damaged: an entry cannot be read as ZIP data");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static ZipArchive Open(string file)
    {
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        }
        catch (InvalidDataException)
        {
            stream.Dispose();
            throw new PackFormatException($"'{Path.GetFileName(file)}' is not a ZIP file, so not an {Extension}.");
        }
    }

    /// <summary>Copies an entry, failing when it holds more or fewer bytes than its header declares (a ZIP bomb lies there).</summary>
    private static async Task CopyBoundedAsync(ZipArchiveEntry entry, Stream output, CancellationToken cancellationToken)
    {
        Stream source = entry.Open();
        await using (source.ConfigureAwait(false))
        {
            byte[] buffer = new byte[1 << 16];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                written += read;
                if (written > entry.Length)
                {
                    throw new PackFormatException($"'{entry.FullName}' holds more bytes than its ZIP header declares.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            if (written != entry.Length)
            {
                throw new PackFormatException($"'{entry.FullName}' holds fewer bytes than its ZIP header declares.");
            }
        }
    }

    private static bool IsLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 // a Unix symbolic link: S_IFLNK in the high word
        || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
}
