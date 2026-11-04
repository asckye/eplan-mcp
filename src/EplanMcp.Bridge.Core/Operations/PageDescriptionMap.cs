namespace EplanMcp.Bridge.Core.Operations;

internal static class PageDescriptionMap
{
    internal static void Check(Dictionary<string, string>? texts, Func<string, bool> knowsLanguage, string at, List<string> violations)
    {
        if (texts is null) { return; }
        if (texts.Count == 0) { violations.Add(at + " must contain at least one language."); }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in texts)
        {
            if (!LanguageCodes.IsCode(entry.Key) || !knowsLanguage(entry.Key) || !seen.Add(entry.Key))
            { violations.Add(at + "." + entry.Key + " must be a unique known language code."); }
            if (entry.Value is null || entry.Value.Length > 500)
            { violations.Add(at + "." + entry.Key + " must be text with at most 500 characters."); }
        }
    }
}
