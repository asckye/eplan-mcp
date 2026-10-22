using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The normalisation of snapshot values (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.3): the same project gives the
/// same text whenever and however often it is opened. Pure logic.
/// </summary>
public static class SnapshotValues
{
    // A runtime string identifier db/type/oid/index (api-notes A32, A36) standing on its own: the database number
    // changes on every opening, the rest does not.
    private static readonly Regex _identifier = new(@"(?<![\d/])\d+/(\d+/\d+/\d+)(?![\d/])", RegexOptions.CultureInvariant);

    // A language code in front of its text, at the start or after the ';' that ends the text before (api-notes A11).
    private static readonly Regex _language = new(@"(?:^|;)([A-Za-z]{2}_[A-Za-z]{2}|\?\?_\?\?)@", RegexOptions.CultureInvariant);

    /// <summary>
    /// Line ends as <c>\n</c>, Unicode in NFC, and every runtime identifier as <c>#type/oid/index</c>, without the
    /// database number.
    /// </summary>
    public static string Text(string raw) =>
        _identifier.Replace(raw.Replace("\r\n", "\n").Replace('\r', '\n').Normalize(NormalizationForm.FormC), "#$1");

    /// <summary>
    /// A raw multi-language value split into one normalised text per language, sorted by code, empty texts and repeated
    /// languages dropped (so possibly none); null when <paramref name="raw"/> does not start with a language code. A text that contains
    /// <c>;xx_YY@</c> is split there (as <see cref="TextLanguages"/>).
    /// </summary>
    public static List<SnapshotText>? Languages(string raw)
    {
        MatchCollection codes = _language.Matches(raw);
        if (codes.Count == 0 || codes[0].Index != 0)
        {
            return null;
        }

        var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < codes.Count; i++)
        {
            int start = codes[i].Index + codes[i].Length;
            int end = i + 1 < codes.Count ? codes[i + 1].Index : raw.EndsWith(";", StringComparison.Ordinal) ? raw.Length - 1 : raw.Length;
            string text = Text(raw.Substring(start, Math.Max(0, end - start)));
            string language = codes[i].Groups[1].Value;
            if (text.Length > 0 && !texts.ContainsKey(language))
            {
                texts[language] = text;
            }
        }

        return [.. texts.Select(t => new SnapshotText { Language = t.Key, Text = t.Value })];
    }

    /// <summary>A position in millimetres as <c>x,y</c>, each rounded to 0.001 away from zero, culture-invariant, no <c>-0</c>.</summary>
    public static string Position(double x, double y) => Millimetres(x) + "," + Millimetres(y);

    /// <summary>
    /// The contract value of one raw value, or null when it is not registered, is a hint, or is empty once normalised.
    /// </summary>
    public static SnapshotValue? Value(SnapshotSourceValue source, IReadOnlyDictionary<int, SnapshotProperty> registered)
    {
        if (!registered.TryGetValue(source.Id, out SnapshotProperty? property) || property.Kind == SnapshotProperty.Hint)
        {
            return null;
        }

        var value = new SnapshotValue { Id = source.Id, Name = property.Name, Index = source.Index };
        if (property.Kind == SnapshotProperty.MultiLanguage && Languages(source.Raw) is { } texts)
        {
            value.Texts = texts;
            return texts.Count == 0 ? null : value;
        }

        value.Value = Text(source.Raw);
        return value.Value.Length == 0 ? null : value;
    }

    /// <summary>The hint of two raw hint values (a date and a time), <c>date time</c>; null when both are missing.</summary>
    public static string? Hint(string? date, string? time)
    {
        string[] parts = [.. new[] { date, time }.Where(p => !string.IsNullOrEmpty(p)).Select(p => Text(p!))];
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static string Millimetres(double value)
    {
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
