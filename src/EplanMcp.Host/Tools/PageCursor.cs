using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Opaque paging cursor: base64url of <c>v2:&lt;offset&gt;:&lt;fingerprint&gt;</c>. The fingerprint hashes the tool, the
/// project and the filters of the call that produced it, so a cursor passed back with other arguments is rejected
/// instead of silently skipping or repeating items (M2 design 4.4). It is only an offset: if the project changes
/// between two calls, pages can shift. Only list tools in this Host create and read it; the model passes it back
/// unchanged (mcp-tool-design 5).
/// </summary>
internal static class PageCursor
{
    private const string Version = "v2";

    /// <summary>Identifies one listing: the tool name followed by the project and filter values as given.</summary>
    public static string Fingerprint(string tool, params string?[] scope)
    {
        // Unit separators keep ("a", "bc") and ("ab", "c") apart; null and "" differ as well.
        string text = tool + string.Concat(scope.Select(part => "\u001f" + (part is null ? "\u0000" : "\u0001" + part)));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash, 0, 8);
    }

    public static string Encode(int offset, string fingerprint) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            Version + ":" + offset.ToString(CultureInfo.InvariantCulture) + ":" + fingerprint));

    /// <summary>
    /// Returns the offset, or null when the cursor was not produced by <see cref="Encode"/> for this
    /// <paramref name="fingerprint"/>: malformed, from an older Host, or from another tool or other filters.
    /// </summary>
    public static int? Decode(string cursor, string fingerprint)
    {
        try
        {
            string[] parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split(':');
            return parts.Length == 3
                && parts[0] == Version
                && parts[2] == fingerprint
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int offset)
                ? offset
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
