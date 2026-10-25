namespace EplanMcp.Spikes.S37;

internal static class ScratchPaths
{
    internal static readonly string Root = Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch")) + Path.DirectorySeparatorChar;

    internal static string Require(string path, string extension)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            throw new ArgumentException("An absolute scratch path is required.");
        }

        if (path.Skip(2).Contains(':') || path.Split('\\', '/').Any(p => p is not ("." or "..")
            && (p.EndsWith(".", StringComparison.Ordinal) || p.EndsWith(" ", StringComparison.Ordinal))))
        {
            throw new ArgumentException("Alternate streams and ambiguous Windows path components are not allowed.");
        }

        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase)
            || full.Substring(Path.GetPathRoot(full)!.Length).IndexOf(':') >= 0
            || full.Split(Path.DirectorySeparatorChar).Any(p => p.EndsWith(".", StringComparison.Ordinal) || p.EndsWith(" ", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Path must be below USERPROFILE/EplanMcp/scratch with the required extension.");
        }

        if (extension != ".elk" && full.Split(Path.DirectorySeparatorChar).Any(part => part.EndsWith(".edb", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Marker and macro files must be outside project databases.");
        }

        // Refuse links, including the scratch root and its ancestors, rather than following them outside scratch.
        for (string? item = full; item is not null; item = Path.GetDirectoryName(item))
        {
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("Reparse points are not allowed.");
            }
        }

        return full;
    }

    internal static long ProjectBytes(string project)
    {
        string database = Path.ChangeExtension(Require(project, ".elk"), ".edb");
        return new FileInfo(project).Length + TreeBytes(database);
    }

    private static long TreeBytes(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("Project database contains a reparse point.");
        }

        long bytes = 0;
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("Project database contains a reparse point.");
            }

            bytes += (attributes & FileAttributes.Directory) != 0 ? TreeBytes(entry) : new FileInfo(entry).Length;
        }

        return bytes;
    }
}
