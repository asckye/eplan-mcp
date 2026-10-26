using System.Text;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Ordinal, nonoverlapping replacement over the original text; replacement text is never rescanned.</summary>
internal static class LiteralTextReplace
{
    internal static (string Text, int Occurrences) Apply(string before, string find, string replacement, bool caseSensitive)
    {
        if (find.Length == 0)
        {
            throw new ArgumentException("The literal to find must not be empty.", nameof(find));
        }

        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var result = new StringBuilder();
        int offset = 0;
        int occurrences = 0;
        int match;
        while ((match = before.IndexOf(find, offset, comparison)) >= 0)
        {
            result.Append(before, offset, match - offset);
            result.Append(replacement);
            offset = match + find.Length;
            occurrences++;
        }

        result.Append(before, offset, before.Length - offset);
        return (result.ToString(), occurrences);
    }
}
