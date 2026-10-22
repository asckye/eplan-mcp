using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Packs;

/// <summary>
/// The hashes a manifest records (<see cref="PackResource.Sha256"/>): a file's SHA-256, and a project's tree hash over
/// its link file and every file of its <c>.edb</c> data folder. Never follows a symbolic link or junction.
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
    /// UTF-8 lines <c>path\tbytes\tsha256\n</c>, sorted ordinally by path.
    /// </summary>
    public static async Task<(string Sha256, long Bytes, int Files)> HashTreeAsync(IEnumerable<(string Relative, string FullPath)> files, CancellationToken cancellationToken)
    {
        var lines = new StringBuilder();
        long bytes = 0;
        int count = 0;
        foreach ((string relative, string fullPath) in files.OrderBy(f => f.Relative, StringComparer.Ordinal))
        {
            (string sha256, long length) = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            lines.Append(CultureInfo.InvariantCulture, $"{relative}\t{length}\t{sha256}\n");
            bytes += length;
            count++;
        }

        return (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString()))), bytes, count);
    }

    /// <summary>A project's files: the link file and everything below <c>&lt;name&gt;.edb</c> beside it, relative to their folder.</summary>
    public static IReadOnlyList<(string Relative, string FullPath)> ProjectFiles(string linkFile)
    {
        var files = new List<(string, string)> { (Path.GetFileName(linkFile), linkFile) };
        var data = new DirectoryInfo(Path.ChangeExtension(linkFile, ".edb"));
        if (data.Exists)
        {
            CheckNoLink(data, data.Name);
            Collect(data, data.Name, files);
        }

        return files;
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
