namespace EplanMcp.Evals.Agent;

/// <summary>A file below the export root of the model's eplanmcp: its path relative to the root and its size.</summary>
internal sealed record ExportedFile(string Path, long Bytes);

/// <summary>
/// The files below the export root of the model's eplanmcp, <c>&lt;data root&gt;\exports\</c> (M4 design 4.4). The
/// server writes every export into a new folder <c>&lt;project&gt;\&lt;UTC time&gt;-&lt;suffix&gt;\</c> and keeps
/// the successful ones, so the files that appear there during an attempt are the attempt's exports.
/// </summary>
internal static class ExportedFiles
{
    /// <summary>Every file below <paramref name="root"/>, sorted by its path relative to it; none before the first export.</summary>
    public static IReadOnlyList<ExportedFile> List(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        // The server refuses links in its export folders; they are not followed here either.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return [.. new DirectoryInfo(root).EnumerateFiles("*", options)
            .Select(f => new ExportedFile(Path.GetRelativePath(root, f.FullName), f.Length))
            .OrderBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>The files of <paramref name="now"/> that <paramref name="before"/> does not have.</summary>
    public static IReadOnlyList<ExportedFile> Since(IReadOnlyList<ExportedFile> before, IReadOnlyList<ExportedFile> now)
    {
        var known = new HashSet<string>(before.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        return [.. now.Where(f => !known.Contains(f.Path))];
    }
}
