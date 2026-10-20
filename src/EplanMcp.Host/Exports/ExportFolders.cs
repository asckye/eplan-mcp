using System.Buffers;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Host.Exports;

/// <summary>
/// The export root and the folders the Host creates in it, one per export (M4 design 4.4, SEC-20, ADR-0012):
/// <c>&lt;data root&gt;\exports\&lt;project&gt;\&lt;UTC time&gt;-&lt;suffix&gt;\</c>. A folder is never reused, and below
/// the root no step may be a symbolic link or junction (the rule of SEC-19); the root itself may be one. Stateless
/// apart from its inputs, so safe to share between calls.
/// </summary>
internal sealed class ExportFolders(HostDataLocations locations, TimeProvider clock)
{
    /// <summary>The length of the random suffix: lower-case letters and digits.</summary>
    public const int SuffixLength = 6;

    /// <summary>The name a project gets when its own cannot be a folder name.</summary>
    public const string FallbackName = "project";

    private const string SuffixCharacters = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly SearchValues<char> _invalid = SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary>
    /// Creates a new, empty folder for one export of <paramref name="project"/>. The project's folder is checked before
    /// anything is created in it, so a link there never leads a new folder elsewhere. Throws
    /// <see cref="ExportFolderException"/> when the folder cannot be created or would not lie physically below the root.
    /// </summary>
    public ExportFolder Create(string project)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(locations.Exports));
            Directory.CreateDirectory(root);
            string parent = Path.Combine(root, FolderName(project));
            Directory.CreateDirectory(parent);
            new ExportFolder(root, parent).CheckPlace();

            string name = clock.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + RandomNumberGenerator.GetString(SuffixCharacters, SuffixLength);
            string path = Path.Combine(parent, name);
            if (Path.Exists(path))
            {
                throw new ExportFolderException("The new export folder exists already, and folders are never reused: " + path);
            }

            Directory.CreateDirectory(path);
            var folder = new ExportFolder(root, path);
            folder.CheckPlace();
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException)
        {
            throw new ExportFolderException("The export folder could not be created.", ex);
        }
    }

    /// <summary>
    /// <paramref name="project"/> as one folder name: characters Windows refuses become <c>_</c>, dots and spaces at
    /// the end are dropped, and a name left empty or reserved for a device becomes <see cref="FallbackName"/>.
    /// </summary>
    public static string FolderName(string? project)
    {
        var name = new StringBuilder(project ?? string.Empty);
        for (int i = 0; i < name.Length; i++)
        {
            if (_invalid.Contains(name[i]))
            {
                name[i] = '_';
            }
        }

        string text = name.ToString().TrimEnd('.', ' ');
        return text.Length == 0 || ExportFileName.IsReserved(text) ? FallbackName : text;
    }

    /// <summary>
    /// How many export folders lie below <paramref name="root"/> and the bytes of all files there, for doctor (M4
    /// design Q8). Links and junctions are not followed and folders that cannot be read are skipped.
    /// </summary>
    public static (int Folders, long Bytes) Usage(string root)
    {
        int folders = 0;
        long bytes = 0;
        var pending = new Stack<(DirectoryInfo Folder, int Depth)>();
        pending.Push((new DirectoryInfo(root), 0));
        while (pending.Count > 0)
        {
            (DirectoryInfo folder, int depth) = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = folder.GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue; // a folder that cannot be read adds nothing
            }

            foreach (FileSystemInfo entry in entries.Where(e => (e.Attributes & FileAttributes.ReparsePoint) == 0))
            {
                if (entry is DirectoryInfo subfolder)
                {
                    folders += depth == 1 ? 1 : 0; // <root>\<project>\<export>
                    pending.Push((subfolder, depth + 1));
                }
                else
                {
                    bytes += ((FileInfo)entry).Length;
                }
            }
        }

        return (folders, bytes);
    }
}
