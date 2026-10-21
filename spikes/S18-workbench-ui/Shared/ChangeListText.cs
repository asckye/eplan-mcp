using System.Globalization;
using System.Text;

namespace EplanMcp.Spikes.S18;

/// <summary>The change list after cleaning: what the card shows, and what it had to leave out.</summary>
internal sealed record CleanChangeList(IReadOnlyList<string> Lines, int MoreLines, int Removed);

/// <summary>
/// Turns change-list text that comes from the project into plain display text (M5 design section 5): control
/// characters and bidirectional formatting characters such as U+202E are removed, line breaks inside one entry become
/// a visible mark, and every line and the whole list have a length limit.
/// </summary>
internal static class ChangeListText
{
    internal const int MaxLineLength = 120;
    internal const int MaxLines = 12;
    internal const string LineBreakMark = " ⏎ ";

    internal static CleanChangeList Clean(IReadOnlyList<string> entries)
    {
        var lines = new List<string>();
        int removed = 0;
        foreach (string entry in entries.Take(MaxLines))
        {
            lines.Add(CleanLine(entry, ref removed));
        }

        return new CleanChangeList(lines, Math.Max(0, entries.Count - MaxLines), removed);
    }

    internal static string CleanLine(string text, ref int removed)
    {
        var builder = new StringBuilder(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '\r')
            {
                builder.Append(LineBreakMark);
                index += index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                continue;
            }

            // LF, NEL, LINE SEPARATOR, PARAGRAPH SEPARATOR.
            if (c is '\n' or (char)0x85 or (char)0x2028 or (char)0x2029)
            {
                builder.Append(LineBreakMark);
                index++;
                continue;
            }

            if (c == '\t')
            {
                builder.Append(' ');
                index++;
                continue;
            }

            // An unpaired surrogate decodes as invalid data and is dropped like a control character.
            if (Rune.DecodeFromUtf16(text.AsSpan(index), out Rune rune, out int consumed) != System.Buffers.OperationStatus.Done
                || IsHidden(rune))
            {
                removed++;
            }
            else
            {
                builder.Append(text, index, consumed);
            }

            index += consumed;
        }

        return Truncate(builder.ToString());
    }

    /// <summary>
    /// Cc covers C0, DEL and C1 controls; Cf covers the bidirectional controls (U+061C, U+200E, U+200F, U+202A to
    /// U+202E, U+2066 to U+2069), zero-width characters, U+FEFF and the tag characters U+E0000 to U+E007F.
    /// </summary>
    private static bool IsHidden(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate;

    private static string Truncate(string line)
    {
        if (line.Length <= MaxLineLength)
        {
            return line;
        }

        int cut = MaxLineLength;
        if (char.IsHighSurrogate(line[cut - 1]))
        {
            cut--;
        }

        return string.Concat(line.AsSpan(0, cut), "…");
    }

    /// <summary>Code points of a string, for the log and the README (U+202E is invisible on screen).</summary>
    internal static string Describe(string text) =>
        string.Join(' ', text.EnumerateRunes().Select(r => r.Value is >= 0x20 and < 0x7F
            ? ((char)r.Value).ToString()
            : string.Create(CultureInfo.InvariantCulture, $"U+{r.Value:X4}")));
}
