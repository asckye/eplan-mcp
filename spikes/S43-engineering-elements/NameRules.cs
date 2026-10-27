namespace EplanMcp.Spikes.S43;

internal static class NameRules
{
    internal const string Link = "-S43LINK";
    internal static string[] InterruptionForms(string plant, string location)
    {
        string p = plant.Length == 0 ? string.Empty : "=" + plant;
        string l = location.Length == 0 ? string.Empty : "+" + location;
        // r2 observed the empty plant separator in '=-S43LINK'; never accept an arbitrary structure/leaf suffix.
        return new[] { p + l + Link, Link, "=" + Link, p + Link, l + Link }.Distinct(StringComparer.Ordinal).ToArray();
    }
    internal static bool MatchesInterruption(string actual, string[] allowed) => allowed.Contains(actual, StringComparer.Ordinal);
}
