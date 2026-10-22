using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EplanMcp.Packs;

/// <summary>A SemVer 2.0.0 version, compared by its precedence (semver.org section 11); build metadata is ignored.</summary>
public sealed partial class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] _prerelease;
    private readonly string _text;

    private SemanticVersion(int major, int minor, int patch, string[] prerelease, string text)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
        _text = text;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public bool IsPrerelease => _prerelease.Length > 0;

    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        Match match = text is null ? Match.Empty : Pattern().Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return false;
        }

        string[] prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        version = new SemanticVersion(major, minor, patch, prerelease, text!);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out SemanticVersion? version) ? version : throw new FormatException($"'{text}' is not a SemVer 2.0.0 version.");

    /// <summary>The same major, minor and patch numbers.</summary>
    public bool SameRelease(SemanticVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    /// <summary>A version without pre-release or build: what <c>^</c> and <c>~</c> count up to.</summary>
    public static SemanticVersion Release(int major, int minor, int patch) =>
        new(major, minor, patch, [], string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{patch}"));

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (result != 0 || (_prerelease.Length == 0 && other._prerelease.Length == 0))
        {
            return result;
        }

        // A pre-release has lower precedence than the release; identifiers compare one by one.
        if (_prerelease.Length == 0 || other._prerelease.Length == 0)
        {
            return _prerelease.Length == 0 ? 1 : -1;
        }

        for (int i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            result = CompareIdentifiers(_prerelease[i], other._prerelease[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', _prerelease));

    public override string ToString() => _text;

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Numeric identifiers compare as numbers and before alphanumeric ones, which compare in ASCII order.</summary>
    private static int CompareIdentifiers(string left, string right)
    {
        bool leftNumeric = left.All(char.IsAsciiDigit), rightNumeric = right.All(char.IsAsciiDigit);
        return (leftNumeric, rightNumeric) switch
        {
            (true, true) => left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(left, right),
        };
    }

    [GeneratedRegex(PackPatterns.SemVer, RegexOptions.ECMAScript)]
    private static partial Regex Pattern();
}
