using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Packs;

/// <summary>
/// The hashes a manifest records (<see cref="PackResource.Sha256"/>): a file's SHA-256, and a project's tree hash over
/// its link file and the files of its <c>.edb</c> data folder. EPLAN rewrites its lock files (<c>*.lck</c>) and the
/// per-user session files <c>User.eod</c>/<c>User.eox</c> when it opens, exports from or closes a project (api-notes A16,
/// A25, A30), so neither is hashed: lock files are no part of a project at all (EPLAN's own backups leave them out,
/// A35), session files travel with the project unhashed. Never follows a symbolic link or junction.
/// </summary>
public static class PackHasher
{
    public static async Task<(string Sha256, long Bytes)> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return (Convert.ToHexStringLower(hash), stream.Length);
        }
    }

    /// <summary>
    /// The tree hash of <paramref name="files"/> (path relative to the link file's folder, full path): SHA-256 of the
    /// UTF-8 lines <c>path\tbytes\tsha256\n</c>, sorted ordinally by path; session files are left out, also from the
    /// bytes and the count.
    /// </summary>
    public static async Task<(string Sha256, long Bytes, int Files)> HashTreeAsync(IEnumerable<(string Relative, string FullPath)> files, CancellationToken cancellationToken)
    {
        var lines = new StringBuilder();
        long bytes = 0;
        int count = 0;
        foreach ((string relative, string fullPath) in files.Where(f => !IsSessionFile(f.Relative)).OrderBy(f => f.Relative, StringComparer.Ordinal))
        {
            (string sha256, long length) = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            lines.Append(CultureInfo.InvariantCulture, $"{relative}\t{length}\t{sha256}\n");
            bytes += length;
            count++;
        }

        return (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString()))), bytes, count);
    }

    /// <summary>
    /// A project's files: the link file and everything below <c>&lt;name&gt;.edb</c> beside it except lock files,
    /// relative to their folder.
    /// </summary>
    public static IReadOnlyList<(string Relative, string FullPath)> ProjectFiles(string linkFile)
    {
        var files = new List<(string Relative, string FullPath)> { (Path.GetFileName(linkFile), linkFile) };
        var data = new DirectoryInfo(Path.ChangeExtension(linkFile, ".edb"));
        if (data.Exists)
        {
            CheckNoLink(data, data.Name);
            Collect(data, data.Name, files);
        }

        return [.. files.Where(f => !IsLockFile(f.Relative))];
    }

    /// <summary>EPLAN's lock files, <c>*.lck</c>, at any depth of a data folder.</summary>
    public static bool IsLockFile(string relative) => relative.EndsWith(".lck", StringComparison.OrdinalIgnoreCase);

    /// <summary>EPLAN's per-user session files, <c>User.eod</c> and <c>User.eox</c>, at any depth of a data folder.</summary>
    public static bool IsSessionFile(string relative)
    {
        int slash = relative.LastIndexOf('/');
        string name = relative[(slash + 1)..];
        return slash >= 0 && (name.Equals("User.eod", StringComparison.OrdinalIgnoreCase) || name.Equals("User.eox", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Every file below <paramref name="directory"/>, as (<paramref name="prefix"/>/relative path, full path); throws for a link.</summary>
    public static void Collect(DirectoryInfo directory, string prefix, List<(string Relative, string FullPath)> files)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            string relative = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
            CheckNoLink(entry, relative);
            if (entry is DirectoryInfo folder)
            {
                Collect(folder, relative, files);
            }
            else
            {
                files.Add((relative, entry.FullName));
            }
        }
    }

    private static void CheckNoLink(FileSystemInfo entry, string relative)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new PackFormatException($"'{relative}' is a symbolic link or junction; packs never follow links.");
        }
    }
}
