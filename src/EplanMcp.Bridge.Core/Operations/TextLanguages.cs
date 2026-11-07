using System.Text.RegularExpressions;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The other languages to read a multi-language text in when it has no text in EPLAN's user interface language and
/// the tool's <c>language</c> was omitted (docs/design/2026-10-19-text-language-fallback.md), and the languages a
/// raw value stores.
/// </summary>
public static class TextLanguages
{
    /// <summary>Tried first: part data mostly has English, and models read it most reliably.</summary>
    public const string English = "en_US";

    /// <summary>The language-independent entry of a raw value (api-notes A11).</summary>
    public const string Independent = "??_??";

    // A language code in front of its text, at the start of the raw value or after the ';' that ends the one before.
    private static readonly Regex _storedLanguage = new("(?:^|;)([A-Za-z]{2}_[A-Za-z]{2})@", RegexOptions.CultureInvariant);

    // As above, and the language-independent code.
    private static readonly Regex _anyStoredLanguage = new(@"(?:^|;)([A-Za-z]{2}_[A-Za-z]{2}|\?\?_\?\?)@", RegexOptions.CultureInvariant);

    private static readonly Regex _entry = new(@"([A-Za-z]{2}_[A-Za-z]{2}|\?\?_\?\?)@([^;]*);", RegexOptions.CultureInvariant);

    // Only real languages, each entry closed by ';': the serialized form of a text written in several languages.
    private static readonly Regex _serialized = new(@"^(?:[A-Za-z]{2}_[A-Za-z]{2}@[^;]*;)+$", RegexOptions.CultureInvariant);

    /// <summary>True for <c>zh_CN@…;en_US@…;</c>. A54 still refuses mixing real languages with <c>??_??</c>.</summary>
    public static bool IsSerialized(string? text) => text is not null && _serialized.IsMatch(text);

    /// <summary>Whether two stored values hold the same texts: the language order and empty entries do not count, and plain text is language-independent.</summary>
    public static bool SameValue(string first, string second) => Entries(first).SequenceEqual(Entries(second));

    private static IEnumerable<KeyValuePair<string, string>> Entries(string value)
    {
        MatchCollection found = _entry.Matches(value);
        return found.Count == 0 || string.Concat(found.Cast<Match>().Select(m => m.Value)) != value
            ? value.Length == 0 ? [] : [new KeyValuePair<string, string>(Independent, value)]
            : found.Cast<Match>().Where(m => m.Groups[2].Length > 0).Select(m => new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value)).OrderBy(e => e.Key, StringComparer.Ordinal);
    }

    /// <summary>A text that serialized several languages reads back as a whole or in the language EPLAN displays.</summary>
    public static bool ReadsBackAs(string requested, string? actual) =>
        requested == actual || (actual is not null && IsSerialized(requested) && requested.Contains("@" + actual + ";"));

    /// <summary>
    /// The languages <paramref name="raw"/> stores, English first and then in their stored order. The raw value is
    /// what EPLAN keeps for a multi-language property, <c>de_DE@…;en_US@…;</c> (api-notes A11). Language-independent
    /// values (<c>??_??@…;</c>) are not listed: EPLAN shows them in every language. A text that happens to contain
    /// <c>;xx_YY@</c> adds a language with no text in it, which costs one lookup and changes nothing.
    /// </summary>
    public static IReadOnlyList<string> Fallbacks(string raw)
    {
        List<string> stored = [.. _storedLanguage.Matches(raw).Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];
        return stored.Remove(English) ? [English, .. stored] : stored;
    }

    /// <summary>
    /// Every language <paramref name="raw"/> stores, <see cref="Independent"/> included, once each in stored order:
    /// what eplan_page_update must keep when it changes one of them (docs/tools/eplan_page_update.md, section 5). As
    /// above, a text containing <c>;xx_YY@</c> adds a language with no text, which a rewrite keeps empty.
    /// </summary>
    public static IReadOnlyList<string> Stored(string? raw) =>
        raw is null ? [] : [.. _anyStoredLanguage.Matches(raw).Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];
}
