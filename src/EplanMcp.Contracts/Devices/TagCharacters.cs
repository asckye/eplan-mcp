using System.Text;

namespace EplanMcp.Contracts;

/// <summary>
/// Unicode tag characters (U+E0000-U+E007F) are invisible in every font, so project text can carry hidden instructions
/// in them (MCP server standard WS-10, WS-28). In UTF-16 each is the pair U+DB40 followed by U+DC00-U+DC7F.
/// </summary>
public static class TagCharacters
{
    /// <summary>The text without tag characters; the same instance when it has none.</summary>
    public static string Remove(string text)
    {
        if (text.IndexOf('\uDB40') < 0)
        {
            return text;
        }

        var clean = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (IsTag(text, i))
            {
                i++;
                continue;
            }
            clean.Append(text[i]);
        }

        return clean.ToString();
    }

    /// <summary>Whether a tag character starts at <paramref name="index"/>.</summary>
    public static bool IsTag(string text, int index) =>
        text[index] == '\uDB40' && index + 1 < text.Length && text[index + 1] is >= '\uDC00' and <= '\uDC7F';
}
