using System.Security;

namespace EplanMcp.Host.Exports;

/// <summary>
/// One folder <see cref="ExportFolders.Create"/> made for one export (SEC-20): where EPLAN writes, what it wrote, and
/// how the folder goes again after a failure. Paths are full; <see cref="Path"/> ends with a separator.
/// </summary>
internal sealed class ExportFolder
{
    internal ExportFolder(string root, string path)
    {
        Root = root;
        Path = System.IO.Path.TrimEndingDirectorySeparator(path) + System.IO.Path.DirectorySeparatorChar;
    }

    /// <summary>The export root, without a separator at the end.</summary>
    public string Root { get; }

    /// <summary>The folder, ending with a separator: what the Bridge gets and the tools return as <c>directory</c>.</summary>
    public string Path { get; }

    /// <summary>
    /// Every file below the folder, sorted by path, as paths relative to it. Throws <see cref="ExportFolderException"/>
    /// when the folder or anything in it is a symbolic link or junction, or lies outside it (SEC-20): such an export
    /// counts as failed.
    /// </summary>
    public IReadOnlyList<ExportFile> Files()
    {
        try
        {
            CheckPlace();
            var files = new List<ExportFile>();
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(Path));
            while (pending.Count > 0)
            {
                foreach (FileSystemInfo entry in pending.Pop().GetFileSystemInfos())
                {
                    string relative = System.IO.Path.GetRelativePath(Path, entry.FullName);
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || System.IO.Path.IsPathRooted(relative) || relative.Split(System.IO.Path.DirectorySeparatorChar)[0] == "..")
                    {
                        throw new ExportFolderException("The export folder holds a link, a junction or a path outside it: " + entry.FullName);
                    }

                    if (entry is DirectoryInfo subfolder)
                    {
                        pending.Push(subfolder);
                    }
                    else
                    {
                        files.Add(new ExportFile(relative, ((FileInfo)entry).Length));
                    }
                }
            }

            return [.. files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new ExportFolderException("The export folder could not be listed: " + Path, ex);
        }
    }

    /// <summary>
    /// Deletes this folder and nothing else, after a failed or cancelled export: a symbolic link or junction inside is
    /// removed itself, never followed. (Directory.Delete with recursive is not used: it refuses a tree that holds a
    /// junction.) False when something could not be deleted, for example because EPLAN still writes there.
    /// </summary>
    public bool TryDelete()
    {
        try
        {
            var folder = new DirectoryInfo(Path);
            if (folder.Exists)
            {
                Delete(folder);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks that the folder lies below <see cref="Root"/> and that every step below the root is an existing folder,
    /// no symbolic link or junction (SEC-19's rule); throws <see cref="ExportFolderException"/> otherwise.
    /// </summary>
    internal void CheckPlace()
    {
        string relative = System.IO.Path.GetRelativePath(Root, Path);
        string[] steps = relative.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (System.IO.Path.IsPathRooted(relative) || steps.Length == 0 || steps.Contains(".") || steps.Contains(".."))
        {
            throw new ExportFolderException("The export folder does not lie below the export root: " + Path);
        }

        string current = Root;
        foreach (string step in steps)
        {
            current = System.IO.Path.Combine(current, step);
            var folder = new DirectoryInfo(current);
            if (!folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ExportFolderException("A step of the export folder is missing, or a symbolic link or junction: " + current);
            }
        }
    }

    private static void Delete(DirectoryInfo folder)
    {
        if ((folder.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (FileSystemInfo entry in folder.GetFileSystemInfos())
            {
                if (entry is DirectoryInfo subfolder)
                {
                    Delete(subfolder);
                }
                else
                {
                    if ((entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == FileAttributes.ReadOnly)
                    {
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    entry.Delete();
                }
            }
        }

        folder.Delete(); // a link or junction: only the link goes
    }
}

/// <summary>One exported file: its path relative to the export folder and its size.</summary>
internal sealed record ExportFile(string Path, long Bytes);

/// <summary>
/// An export folder could not be created or listed, or held what an export must not leave (SEC-20). The message may
/// name local paths, so it goes to the log only, never to the model (SEC-12).
/// </summary>
internal sealed class ExportFolderException : Exception
{
    public ExportFolderException()
    {
    }

    public ExportFolderException(string message)
        : base(message)
    {
    }

    public ExportFolderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
