using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Opaque paging cursor: base64url of <c>v1:&lt;offset&gt;</c>. Only list tools in this Host create and read it;
/// the model must pass it back unchanged (mcp-tool-design 5).
/// </summary>
internal static class PageCursor
{
    private const string Version = "v1:";

    public static string Encode(int offset) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(Version + offset.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Returns the offset, or null when the cursor was not produced by <see cref="Encode"/>.</summary>
    public static int? Decode(string cursor)
    {
        try
        {
            string text = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor));
            return text.StartsWith(Version, StringComparison.Ordinal)
                && int.TryParse(text.AsSpan(Version.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int offset)
                ? offset
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
