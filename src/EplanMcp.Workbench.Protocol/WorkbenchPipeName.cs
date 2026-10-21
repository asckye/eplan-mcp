using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// The Workbench's pipe name (ADR-0014 decision 2): <c>EplanMcp.Workbench.1.&lt;digest&gt;</c>, the digest being the first
/// 32 hex digits of SHA-256 over "user SID, line feed, the data root's normalized full path in upper case". Other users
/// and other data roots, such as a Large test's temporary one, get other names.
/// </summary>
public static class WorkbenchPipeName
{
    /// <summary>The name for the current Windows user and <paramref name="dataRoot"/>.</summary>
    public static string ForCurrentUser(string dataRoot)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current Windows identity has no user SID.");
        return For(user.Value, dataRoot);
    }

    /// <summary>The name for user <paramref name="userSid"/> and <paramref name="dataRoot"/>.</summary>
    public static string For(string userSid, string dataRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)).ToUpperInvariant();
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(userSid + "\n" + root));
        return WorkbenchProtocol.PipePrefix + Convert.ToHexStringLower(digest)[..32];
    }
}
