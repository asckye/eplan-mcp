using System.Globalization;
using System.Text.RegularExpressions;

namespace EplanMcp.Bridge.Core.Operations;

internal static class PropertyReadText
{
    // 250 ms bounds backtracking; 50 ms timed out under load (first-use compilation while the test suites ran in
    // parallel) and hid ordinary values, since a timeout returns null.
    private static readonly Regex _sensitive = new(
        @"[\\]|//|[a-z]:/|(?:^|\s)/[\w.]|[\w.+%-]+@[\w.-]+|(?:mailto|tel|https?|file):|(?<!\w)\+?\d[\d ()-]{6,}\d(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    // Same CleanText controls and marker as Host ResultShaper; inspect the full view before truncation (SEC-08).
    internal static string? CleanText(string? text, out int length)
    {
        string? clean = text is null ? null : new string([.. text.Where(c =>
            (!char.IsControl(c) || c is '\n' or '\t') && c is not (>= '\u202a' and <= '\u202e') and not (>= '\u2066' and <= '\u2069'))]);
        length = clean?.Length ?? 0;
        try
        {
            if (clean is null || _sensitive.IsMatch(clean))
            {
                return null;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return null; // An unclassified view must never escape through a timeout.
        }

        int cut = clean.Length > 500 && char.IsHighSurrogate(clean[499]) ? 499 : 500;
        return clean.Length <= 500 ? clean : clean.Substring(0, cut)
            + " … [truncated, " + clean.Length.ToString(CultureInfo.InvariantCulture) + " characters in total]";
    }
}
