using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EplanMcp.Packs;

/// <summary>
/// EPLAN versions a pack works with. EPLAN versions are one to four numbers (<c>2026.0.3.25421</c>), not SemVer.
/// Comparators separated by spaces must all hold: <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c> compare with
/// missing numbers as 0; a version without an operator, or with <c>=</c>, matches by prefix, so <c>2026.0</c> is every
/// 2026.0.x; <c>*</c> alone is any version.
/// </summary>
public sealed partial class EplanVersionRange
{
    private static readonly string[] _operators = [">=", "<=", ">", "<", "="];

    private readonly (string Operator, int[] Version)[] _comparators;
    private readonly string _text;

    private EplanVersionRange((string, int[])[] comparators, string text)
    {
        _comparators = comparators;
        _text = text;
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out EplanVersionRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256)
        {
            return false;
        }

        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var comparators = new List<(string, int[])>();
        foreach (string token in tokens is ["*"] ? [] : tokens)
        {
            string op = _operators.FirstOrDefault(o => token.StartsWith(o, StringComparison.Ordinal)) ?? "=";
            if (!TryParseVersion(token[(token.StartsWith(op, StringComparison.Ordinal) ? op.Length : 0)..], out int[]? version))
            {
                return false;
            }

            comparators.Add((op, version));
        }

        range = new EplanVersionRange([.. comparators], text);
        return true;
    }

    /// <summary>One to four numbers separated by dots (<see cref="PackPatterns.EplanVersion"/>).</summary>
    public static bool TryParseVersion(string? text, [NotNullWhen(true)] out int[]? version)
    {
        version = text is not null && Pattern().IsMatch(text)
            ? [.. text.Split('.').Select(p => int.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture))]
            : null;
        return version is not null;
    }

    public bool Contains(string version) =>
        TryParseVersion(version, out int[]? parts) && _comparators.All(c => c.Operator == "="
            ? c.Version.Select((n, i) => n == (i < parts.Length ? parts[i] : 0)).All(same => same)
            : VersionRange.Holds(c.Operator, Compare(parts, c.Version)));

    public override string ToString() => _text;

    private static int Compare(int[] left, int[] right)
    {
        for (int i = 0; i < 4; i++)
        {
            int result = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    [GeneratedRegex(PackPatterns.EplanVersion, RegexOptions.ECMAScript)]
    private static partial Regex Pattern();
}
