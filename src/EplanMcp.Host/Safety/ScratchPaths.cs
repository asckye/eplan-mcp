using System.Runtime.InteropServices;

namespace EplanMcp.Host.Safety;

/// <summary>
/// Whether a project lies in a scratch root (SEC-05, ADR-0005 section 2). Both sides are compared as canonical paths:
/// <c>..</c> resolved, 8.3 short names expanded, symbolic links and junctions followed to where they really point, case
/// ignored. Anything that cannot be resolved counts as outside, so a path trick can only make a scratch project look
/// real, never the other way round.
/// </summary>
internal static class ScratchPaths
{
    public static bool IsInside(string path, IEnumerable<string> roots)
    {
        string candidate;
        try
        {
            candidate = Canonical(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        foreach (string root in roots)
        {
            string canonicalRoot;
            try
            {
                canonicalRoot = Canonical(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (candidate.Length > canonicalRoot.Length
                && candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase)
                && candidate[canonicalRoot.Length] == Path.DirectorySeparatorChar)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The path as the file system resolves it; throws when a link cannot be followed (a cycle, for example).</summary>
    public static string Canonical(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // Network and device paths (\\server\share, \\?\...) are not scratch roots; refuse rather than guess.
            throw new NotSupportedException("Network and device paths are not supported.");
        }

        string current = Path.GetPathRoot(full)!;
        string[] parts = full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string next = Path.Combine(current, parts[i]);
            var directory = new DirectoryInfo(next);
            FileSystemInfo entry = directory.Exists ? directory : new FileInfo(next);
            if (!entry.Exists)
            {
                // Nothing from here on exists, so nothing further can be a link or a short name.
                return Path.TrimEndingDirectorySeparator(Path.Combine(current, Path.Combine(parts[i..])));
            }

            current = entry.LinkTarget is not null
                ? LongName(Path.GetFullPath((entry.ResolveLinkTarget(returnFinalTarget: true) ?? entry).FullName))
                : LongName(next);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>The long form of an existing path's short (8.3) names.</summary>
    private static string LongName(string existing)
    {
        char[] buffer = new char[32_768];
        int length = GetLongPathName(existing, buffer, buffer.Length);
        return length > 0 && length < buffer.Length ? new string(buffer, 0, length) : existing;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode)]
    private static extern int GetLongPathName(string shortPath, [Out] char[] longPath, int bufferLength);
}
