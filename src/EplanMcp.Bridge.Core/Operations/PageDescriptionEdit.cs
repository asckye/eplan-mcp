namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Changing one language of a description without losing the others (docs/tools/eplan_page_update.md, section 5):
/// assigning a value replaces every language (api-notes A13), so the new value is built from all stored languages,
/// and the value read back is checked against that. An empty text counts as no text.
/// </summary>
public static class PageDescriptionEdit
{
    /// <summary>
    /// <paramref name="entries"/> with <paramref name="language"/>'s text replaced where it stands, added at the end
    /// when it is missing, or left out when <paramref name="text"/> is null; the other entries unchanged, in order.
    /// </summary>
    public static List<TextEntry> With(IReadOnlyList<TextEntry> entries, string language, string? text)
    {
        var result = new List<TextEntry>();
        bool placed = false;
        foreach (TextEntry entry in entries)
        {
            if (!Same(entry.Language, language))
            {
                result.Add(entry);
            }
            else if (text is not null && !placed)
            {
                result.Add(new TextEntry(entry.Language, text));
                placed = true;
            }
        }

        if (text is not null && !placed)
        {
            result.Add(new TextEntry(language, text));
        }

        return result;
    }

    /// <summary>
    /// Null when <paramref name="after"/> has <paramref name="text"/> in <paramref name="language"/> (nothing when null),
    /// every other language of <paramref name="before"/> with its text and no other language; otherwise the first
    /// language that differs.
    /// </summary>
    public static string? Difference(PageDescription before, PageDescription after, string language, string? text)
    {
        if (!string.Equals(after.TextIn(language), text, StringComparison.Ordinal))
        {
            return language;
        }

        IEnumerable<string> others = before.Entries.Concat(after.Entries).Select(e => e.Language).Where(code => !Same(code, language));
        return others.FirstOrDefault(code => !string.Equals(before.TextIn(code), after.TextIn(code), StringComparison.Ordinal));
    }

    private static bool Same(string code, string language) => string.Equals(code, language, StringComparison.OrdinalIgnoreCase);
}
