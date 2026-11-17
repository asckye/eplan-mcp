namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The form of the full paths the Host hands the M6 project operations (docs/design/2026-10-21-m6-bridge-snapshot.md,
/// 4.1, 4.8): a local drive path, never a UNC share or device path (EPLAN would send the user's credential hash to a
/// share, as SEC-19 says of macros), without <c>.</c> or <c>..</c> segments or a second colon. Pure logic.
/// </summary>
public static class ProjectPaths
{
    /// <summary>Whether <paramref name="path"/> is such a path: <c>X:\…</c>.</summary>
    public static bool IsPlainFullPath(string? path)
    {
        if (path is not { Length: >= 3 } || !(path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') || path[1] != ':' || path[2] is not ('\\' or '/'))
        {
            return false;
        }

        if (path.IndexOf(':', 2) >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOfAny(['*', '?']) >= 0)
        {
            return false;
        }

        return !path.Substring(3).Split('\\', '/').Any(segment => segment is "." or "..");
    }
}
