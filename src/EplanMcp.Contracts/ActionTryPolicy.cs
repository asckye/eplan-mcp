using System.Text.RegularExpressions;

namespace EplanMcp.Contracts;

/// <summary>Shipped, fail-closed exclusions shared by Host and Bridge; expressions are never caller supplied.</summary>
public static class ActionTryPolicy
{
    private static readonly string[][] _rules = Load();
    private static string[][] Load()
    {
        using Stream stream = typeof(ActionTryPolicy).Assembly.GetManifestResourceStream("EplanMcp.Contracts.ActionTryDeny.tsv")
            ?? throw new InvalidOperationException("Missing action trial exclusions.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Where(s => s.Length > 0 && !s.StartsWith("#", StringComparison.Ordinal)).Select(s => s.TrimEnd('\r').Split('\t')).ToArray();
    }
    public static bool Denied(string kind, string value) => _rules.Any(r => r[0] == kind && Regex.IsMatch(value, r[1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    public static bool Name(string? value) => value is { Length: > 0 and <= 100 } && Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_.]*\z", RegexOptions.CultureInvariant);
    public static bool Value(string? value) => value is { Length: <= 100 } && !value.Any(c => char.IsControl(c) || c is '/' or '\\' or '"' or '\'' or ':' or '%' or '$')
        && value.IndexOf("..", StringComparison.Ordinal) < 0;
}
