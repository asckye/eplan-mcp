using System.Text.RegularExpressions;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The form of a structure identifier's full name: a block's prefix, then the name EPLAN holds.</summary>
public static class StructureNames
{
    /// <summary>The prefixes of the blocks, the longer of two alike first.</summary>
    public static readonly IReadOnlyList<string> Prefixes = ["==", "=", "++", "+", "&", "#"];

    private static readonly Regex _form = new(@"^(==|=|\+\+|\+|&|#)[^\s=+&#/\-][^\s=+&#/]*$", RegexOptions.CultureInvariant);

    public static bool IsValid(string? name) => name is { Length: <= 100 } && _form.IsMatch(name);

    /// <summary>The block's prefix of a full name; empty when it has none.</summary>
    public static string Prefix(string name) => Prefixes.FirstOrDefault(p => name.StartsWith(p, StringComparison.Ordinal)) ?? string.Empty;
}
