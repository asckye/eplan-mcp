using System.Buffers;

namespace EplanMcp.Packs;

/// <summary>
/// A path inside a pack: relative, <c>/</c> between folders, every step a name Windows accepts as it is (the rules of
/// SEC-19). Two paths that differ only in case name the same file on Windows, so packs compare paths with
/// <see cref="Comparer"/>.
/// </summary>
public static class PackPath
{
    public const int MaxLength = 240;

    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    private static readonly SearchValues<char> _invalid = SearchValues.Create("<>:\"|?*\\");

    private static readonly HashSet<string> _reserved = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>What is wrong with <paramref name="path"/>, or null when it is a valid pack path.</summary>
    public static string? Problem(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "is empty";
        }

        if (path.Length > MaxLength)
        {
            return $"is longer than {MaxLength} characters";
        }

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            return "uses '\\'; pack paths use '/' between folders";
        }

        foreach (string step in path.Split('/'))
        {
            if (step.Length == 0)
            {
                return "has an empty step: a leading or trailing '/', or '//'";
            }

            if (step is "." or "..")
            {
                return "has a '.' or '..' step";
            }

            if (step.AsSpan().ContainsAny(_invalid) || step.Any(char.IsControl))
            {
                return $"has a character Windows does not allow in a name in '{step}'";
            }

            // Windows drops dots and spaces at the end of a name, so such a step could name another one.
            if (step.EndsWith('.') || step.EndsWith(' ') || IsReserved(step))
            {
                return $"has a name Windows changes or reserves: '{step}'";
            }
        }

        return null;
    }

    /// <summary>A device name such as <c>NUL</c>, also with an extension (<c>nul.txt</c>).</summary>
    public static bool IsReserved(string name) => _reserved.Contains(name.Split('.')[0].TrimEnd(' '));
}
