using System.Text;

namespace EplanMcp.Workbench.Approvals;

/// <summary>A change list as shown: its cleaned lines, and how many more the limit left out.</summary>
internal sealed record CleanList(IReadOnlyList<string> Lines, int MoreLines);

/// <summary>
/// Project and client text made plain before it is shown (workbench-ui APR-08, CTL-12, SEC-23, T20), by the Host's
/// rules and a few more. Removed: C0, DEL and C1 control characters, the bidirectional embedding, override and isolate
/// controls (U+202A to U+202E, U+2066 to U+2069) and marks (U+200E, U+200F, U+061C), and half a surrogate pair. A line
/// break inside one entry shows as a visible mark and a tab as a space, so one entry stays one line. Lines and the list
/// have length limits. The Host cleans the same text first; the Workbench does not count on it.
/// </summary>
internal static class ProjectText
{
    /// <summary>The longest line shown whole: above the Host's longest change, two values of 120 characters in words.</summary>
    public const int MaxLineLength = 400;

    /// <summary>The most lines a change list shows; the rest are counted (APR-08).</summary>
    public const int MaxLines = 200;

    /// <summary>What a line break inside one entry shows as.</summary>
    public const string LineBreak = " ⏎ ";

    /// <summary>What ends a line cut at the limit.</summary>
    public const string Cut = "…";

    /// <summary>The list's entries as lines, one each, empty ones kept as separators, at most <see cref="MaxLines"/>.</summary>
    public static CleanList List(IReadOnlyList<string> entries) =>
        new([.. entries.Take(MaxLines).Select(entry => Line(entry))], Math.Max(0, entries.Count - MaxLines));

    /// <summary>One text as one plain line of at most <paramref name="maxLength"/> characters.</summary>
    public static string Line(string text, int maxLength = MaxLineLength)
    {
        var line = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')
            {
                line.Append(LineBreak);
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 1 : 0;
            }
            else if (c == '\t')
            {
                line.Append(' ');
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                line.Append(c).Append(text[++i]);
            }
            else if (!char.IsControl(c) && !char.IsSurrogate(c) && !IsBidiControl(c))
            {
                line.Append(c);
            }
        }

        if (line.Length <= maxLength)
        {
            return line.ToString();
        }

        int cut = char.IsHighSurrogate(line[maxLength - 1]) ? maxLength - 1 : maxLength;
        return line.ToString(0, cut) + Cut;
    }

    private static bool IsBidiControl(char c) => c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069') or '\u200E' or '\u200F' or '\u061C';
}
