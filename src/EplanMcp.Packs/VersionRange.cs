using System.Diagnostics.CodeAnalysis;

namespace EplanMcp.Packs;

/// <summary>
/// The pack versions a dependency accepts: comparators separated by spaces that must all hold. <c>1.2.3</c> or
/// <c>=1.2.3</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c>; <c>^1.2.3</c> up to the next major version (below
/// 1.0.0: the next minor, below 0.1.0: the next patch); <c>~1.2.3</c> up to the next minor version; <c>*</c> alone for any
/// release. As in npm, a pre-release is in the range only when a comparator names a pre-release of the same
/// major.minor.patch.
/// </summary>
public sealed class VersionRange
{
    /// <summary>Two-character operators first, so <c>&gt;=</c> is not read as <c>&gt;</c>.</summary>
    private static readonly string[] _operators = [">=", "<=", ">", "<", "=", "^", "~"];

    private readonly (string Operator, SemanticVersion Version)[] _comparators;
    private readonly string _text;

    private VersionRange((string, SemanticVersion)[] comparators, string text)
    {
        _comparators = comparators;
        _text = text;
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256)
        {
            return false;
        }

        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var comparators = new List<(string, SemanticVersion)>();
        foreach (string token in tokens is ["*"] ? [] : tokens)
        {
            string op = _operators.FirstOrDefault(o => token.StartsWith(o, StringComparison.Ordinal)) ?? string.Empty;
            if (!SemanticVersion.TryParse(token[op.Length..], out SemanticVersion? version))
            {
                return false;
            }

            switch (op)
            {
                case "^":
                    comparators.Add((">=", version));
                    comparators.Add(("<", version.Major > 0 ? SemanticVersion.Release(version.Major + 1, 0, 0)
                        : version.Minor > 0 ? SemanticVersion.Release(0, version.Minor + 1, 0)
                        : SemanticVersion.Release(0, 0, version.Patch + 1)));
                    break;
                case "~":
                    comparators.Add((">=", version));
                    comparators.Add(("<", SemanticVersion.Release(version.Major, version.Minor + 1, 0)));
                    break;
                default:
                    comparators.Add((op.Length == 0 ? "=" : op, version));
                    break;
            }
        }

        range = new VersionRange([.. comparators], text);
        return true;
    }

    public bool Contains(SemanticVersion version) =>
        (!version.IsPrerelease || _comparators.Any(c => c.Version.IsPrerelease && c.Version.SameRelease(version)))
        && _comparators.All(c => Holds(c.Operator, version.CompareTo(c.Version)));

    public override string ToString() => _text;

    internal static bool Holds(string op, int comparison) => op switch
    {
        "=" => comparison == 0,
        ">" => comparison > 0,
        ">=" => comparison >= 0,
        "<" => comparison < 0,
        _ => comparison <= 0,
    };
}
