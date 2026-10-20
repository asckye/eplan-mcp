using System.Text.RegularExpressions;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The other languages to read a multi-language text in when it has no text in EPLAN's user interface language and
/// the tool's <c>language</c> was omitted (docs/design/2026-10-19-text-language-fallback.md).
/// </summary>
public static class TextLanguages
{
    /// <summary>Tried first: part data mostly has English, and models read it most reliably.</summary>
    public const string English = "en_US";

    // A language code in front of its text, at the start of the raw value or after the ';' that ends the one before.
    private static readonly Regex _storedLanguage = new("(?:^|;)([A-Za-z]{2}_[A-Za-z]{2})@", RegexOptions.CultureInvariant);

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
}
