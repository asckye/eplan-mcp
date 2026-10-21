namespace EplanMcp.Host.Addin;

/// <summary>
/// Whether an add-in path lies in another Windows user's profile (M5 design 4.2, Q15). EPLAN keeps add-in registrations
/// per machine (api-notes A23), so registering over another user's installation would break EplanMcp for that user.
/// install-addin learns the path from the handshake of the EPLAN it started (<see cref="EplanMcp.Contracts.HandshakeResult.AddinPath"/>):
/// EPLAN's settings keep it compressed (A23), so they are not read. It sees another user's registration only when that
/// user's add-in loads in this user's EPLAN; one this user may not read fails to load and is not seen (design 4.2).
/// </summary>
internal static class OtherUserRegistration
{
    /// <summary>
    /// True for a path below the profiles folder (the parent of <paramref name="userProfile"/>) that is in neither this
    /// user's profile nor the shared <paramref name="publicFolder"/>. Paths are compared in full form, ignoring case.
    /// </summary>
    public static bool InAnotherProfile(string path, string userProfile, string publicFolder)
    {
        string? full = FullPath(path);
        string own = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userProfile));
        string profiles = Path.GetDirectoryName(own) + Path.DirectorySeparatorChar;
        return full is not null
            && full.StartsWith(profiles, StringComparison.OrdinalIgnoreCase)
            && !Within(full, own)
            && !Within(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(publicFolder)));
    }

    private static bool Within(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>The full form of a path EPLAN reported (forward slashes, <c>..</c>), or null for one that is not a full path.</summary>
    private static string? FullPath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
