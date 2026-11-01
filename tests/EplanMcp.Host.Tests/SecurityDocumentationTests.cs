using System.Text.RegularExpressions;

namespace EplanMcp.Host.Tests;

[Trait("Size", "Small")]
public sealed partial class SecurityDocumentationTests
{
    [Fact]
    public void EveryNamedThreat_HasStrideRowAndReferencingSecurityMeasure()
    {
        using Stream stream = typeof(SecurityDocumentationTests).Assembly.GetManifestResourceStream("SecurityDocs.security.md")
            ?? throw new InvalidOperationException("Missing security documentation resource.");
        using var reader = new StreamReader(stream);

        Assert.Empty(Problems(reader.ReadToEnd()));
    }

    [Theory]
    [InlineData("T2\n| **SEC-01** | T2 |", "T2: missing STRIDE row")]
    [InlineData("| T2 | Threat | SEC-01 |\n| **SEC-01** | T20 |", "T2: no SEC entry references it")]
    public void IncompleteThreatDocumentation_IsReported(string text, string expected)
    {
        Assert.Contains(expected, Problems(text));
    }

    private static IEnumerable<string> Problems(string text)
    {
        string[] rows = text.Split('\n');
        string[] measures = [.. rows.Where(row => Regex.IsMatch(row, @"^\| (?:\*\*)?SEC-\d+"))];
        foreach (string threat in Threat().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal))
        {
            if (!rows.Any(row => row.StartsWith("| " + threat + " |", StringComparison.Ordinal)))
            {
                yield return threat + ": missing STRIDE row";
            }

            if (!measures.Any(row => Threat().Matches(row).Any(match => match.Value == threat)))
            {
                yield return threat + ": no SEC entry references it";
            }
        }
    }

    [GeneratedRegex(@"\bT\d+\b")]
    private static partial Regex Threat();
}
