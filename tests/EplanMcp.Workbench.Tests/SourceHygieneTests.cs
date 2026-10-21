namespace EplanMcp.Workbench.Tests;

/// <summary>
/// The Workbench's own source holds no invisible character that could make code read differently from what it does
/// ("Trojan Source"): the characters the change list strips are written as escapes in the code that strips them.
/// </summary>
[Trait("Size", "Small")]
public sealed class SourceHygieneTests
{
    public static TheoryData<string> Files => [.. SourceScan.Markup.Concat(SourceScan.Code).Select(f => f.Path)];

    [Theory]
    [MemberData(nameof(Files))]
    public void NoSourceFile_HoldsBidirectionalOrControlCharacters(string path)
    {
        string text = SourceScan.Markup.Concat(SourceScan.Code).Single(f => f.Path == path).Text;

        Assert.DoesNotContain(text, c => (char.IsControl(c) && c is not ('\r' or '\n' or '\t')) || c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069')
            or '\u200E' or '\u200F' or '\u061C' or '\u2028' or '\u2029');
    }
}
