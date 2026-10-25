using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Host-only grammar: literal page structures and labels, with ? or ASCII digits at macro placeholders.</summary>
internal static class PlanNamePattern
{
    public static string Name(string page, PlanMacroFunction function)
    {
        // Only the structure kinds used by this function follow the page (A13/A14).
        string structure = string.Concat(function.StructureSegments.Select(s =>
            Regex.Match(page.Split('/')[0], Regex.Escape(s[..1]) + @"[^=+&]+", RegexOptions.CultureInvariant).Value));
        int dash = function.Name.IndexOf('-', StringComparison.Ordinal);
        return structure + (dash < 0 ? "" : function.Name[dash..]);
    }

    public static string For(string page, PlanMacroFunction function)
    {
        string name = Name(page, function);
        int dash = function.Name.IndexOf('-', StringComparison.Ordinal);
        string label = dash < 0 ? "" : function.Name[dash..];
        string structure = name[..(name.Length - label.Length)];
        return @"\A" + Regex.Escape(structure) + Label(label) + @"\z";
    }

    public static string Label(string label) => string.Join(@"(?:\?|[0-9]+)", label.Split('?').Select(Regex.Escape));

    public static string Visible(PlanMacroFunction function)
    {
        int dash = function.VisibleName.IndexOf('-', StringComparison.Ordinal);
        return @"\A" + (dash < 0 ? Regex.Escape(function.VisibleName)
            : Regex.Escape(function.VisibleName[..dash]) + Label(function.VisibleName[dash..])) + @"\z";
    }

    public static bool Matches(string pattern, string name) => Regex.IsMatch(name, pattern,
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
}
