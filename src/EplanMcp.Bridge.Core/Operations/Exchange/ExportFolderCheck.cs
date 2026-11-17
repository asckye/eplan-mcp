using System.Security;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The folder the Host created for one export, as the Bridge sees it (SEC-20): checked to be there and empty before
/// EPLAN writes, and listed after each image to find the file EPLAN added. Links are never followed.
/// </summary>
public static class ExportFolderCheck
{
    /// <summary>
    /// The full path of <paramref name="directory"/>, which must be a fully qualified path of an existing, empty folder
    /// that is no symbolic link or junction; otherwise INTERNAL, because the Host always creates a new one.
    /// </summary>
    public static string CheckEmpty(string? directory)
    {
        string path = directory ?? string.Empty;
        bool qualified = path.StartsWith(@"\\", StringComparison.Ordinal) || (path.Length >= 3 && path[1] == ':' && path[2] is '\\' or '/');
        DirectoryInfo? folder = qualified ? new DirectoryInfo(path) : null;
        return folder is { Exists: true } && (folder.Attributes & FileAttributes.ReparsePoint) == 0 && !folder.EnumerateFileSystemInfos().Any()
            ? folder.FullName
            : throw new BridgeErrorException(ErrorReason.Internal, "The folder the server created for this export is missing or not empty, so nothing was exported. Please report it.");
    }

    /// <summary>Every file below <paramref name="directory"/>, as a path relative to it with <c>\</c> between folders; none when it is missing.</summary>
    public static HashSet<string> Files(string directory)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(DirectoryInfo Folder, string Path)>();
        pending.Push((new DirectoryInfo(directory), string.Empty));
        while (pending.Count > 0)
        {
            (DirectoryInfo folder, string folderPath) = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = folder.GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue; // missing or unreadable: no files the export could name
            }

            foreach (FileSystemInfo entry in entries.Where(e => (e.Attributes & FileAttributes.ReparsePoint) == 0))
            {
                string path = folderPath.Length == 0 ? entry.Name : folderPath + "\\" + entry.Name;
                if (entry is DirectoryInfo subfolder)
                {
                    pending.Push((subfolder, path));
                }
                else
                {
                    files.Add(path);
                }
            }
        }

        return files;
    }
}
