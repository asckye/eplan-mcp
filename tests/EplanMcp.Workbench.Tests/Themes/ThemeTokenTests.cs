using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EplanMcp.Workbench.Tests.Themes;

/// <summary>PRI-04: scan the real views, including setters, and load the palettes the application actually swaps.</summary>
[Trait("Size", "Small")]
public sealed partial class ThemeTokenTests
{
    private static readonly XName _key = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
    private static readonly string[] _palettes = ["Light", "Dark", "HighContrast"];
    private static readonly string[] _iconTypes = ["Path", "Image", "Ellipse", "Rectangle", "Polygon", "Polyline"];

    private static IEnumerable<SourceFile> Views => SourceScan.Own.Where(f => f.Path.EndsWith(".xaml", StringComparison.Ordinal)
        && !f.Path.StartsWith("Themes/", StringComparison.Ordinal));

    [Fact]
    public void Views_HaveNoLiteralSpacingOrIconSize() =>
        Assert.Empty(Views.SelectMany(file => LiteralDimensions(file.Text).Select(value => $"{file.Path}: {value}")));

    [Fact]
    public void ViewCode_UsesThicknessResourcesExceptForZero() =>
        Assert.Empty(SourceScan.Own.Where(f => f.Path.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => NonzeroThickness().Matches(f.Text).Select(m => $"{f.Path}: {m.Value}")));

    [Theory]
    [InlineData("<Border Margin='0,2,8,0' />")]
    [InlineData("<Setter Property='Padding' Value='8' />")]
    [InlineData("<Border><Border.Margin><Thickness Left='8' /></Border.Margin></Border>")]
    [InlineData("<Path Width='14' />")]
    [InlineData("<Style TargetType='Path'><Setter Property='Height' Value='16' /></Style>")]
    [InlineData("<Image><Image.Width>20</Image.Width></Image>")]
    public void Scan_RejectsAttributesSettersAndPropertyElements(string markup) => Assert.NotEmpty(LiteralDimensions(markup));

    [Theory]
    [InlineData("<Border Margin='0' Padding='{StaticResource Ui.Inset.Input}' />")]
    [InlineData("<Path Width='{StaticResource Ui.IconSize.Small}' Height='0' />")]
    [InlineData("<Border Padding='{TemplateBinding Padding}' />")]
    public void Scan_AllowsZeroTokensAndTemplateForwarding(string markup) => Assert.Empty(LiteralDimensions(markup));

    [Fact]
    public void Views_UseSemanticBrushAliases_InMarkupAndCode()
    {
        HashSet<string> paletteBrushes = SourceScan.Markup.Where(f => f.Path.StartsWith("Tia/Themes/Palette.", StringComparison.Ordinal))
            .SelectMany(f => XDocument.Parse(f.Text).Root!.Elements()).Select(e => (string)e.Attribute(_key)!).ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> markup = Views.SelectMany(f => ResourceReference().Matches(f.Text)
            .Where(m => paletteBrushes.Contains(m.Groups[1].Value)).Select(m => $"{f.Path}: {m.Groups[1].Value}"));
        IEnumerable<string> code = SourceScan.Own.Where(f => f.Path.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => StringLiteral().Matches(f.Text).Where(m => paletteBrushes.Contains(m.Groups[1].Value))
                .Select(m => $"{f.Path}: {m.Groups[1].Value}"));

        Assert.Empty(markup.Concat(code));
    }

    [Fact]
    public void EveryAlias_IsDeclaredInAllThreePalettes_AndEveryViewAliasExists()
    {
        string[][] aliases = [.. _palettes.Select(name => Aliases(name).Select(e => (string)e.Attribute(_key)!).Order(StringComparer.Ordinal).ToArray())];
        Assert.NotEmpty(aliases[0]);
        Assert.Equal(aliases[0], aliases[1]);
        Assert.Equal(aliases[0], aliases[2]);
        string[] references = [.. Views.SelectMany(f => ResourceReference().Matches(f.Text).Select(m => m.Groups[1].Value))
            .Concat(SourceScan.Own.Where(f => f.Path.EndsWith(".cs", StringComparison.Ordinal))
                .SelectMany(f => StringLiteral().Matches(f.Text).Select(m => m.Groups[1].Value)))
            .Where(value => AliasKey().IsMatch(value)).Distinct(StringComparer.Ordinal)];
        Assert.All(references, alias => Assert.Contains(alias, aliases[0]));
    }

    [Fact]
    public void HighContrast_OverridesTheSelectedPairAndSecondaryText_OutsideTheVendoredPalette()
    {
        XDocument palette = XDocument.Parse(SourceScan.Single("Themes/Palette.HighContrast.xaml").Text);
        AssertMapping("Primer.cardSel", "HighlightColorKey");
        AssertMapping("Primer.selectedText", "HighlightTextColorKey");
        AssertMapping("Primer.textFaint", "WindowTextColorKey");

        void AssertMapping(string name, string systemKey) => Assert.Equal($"{{DynamicResource {{x:Static SystemColors.{systemKey}}}}}",
            (string?)palette.Root!.Elements().Single(e => (string?)e.Attribute(_key) == name).Attribute("Color"));
    }

    [Fact]
    public void TextEmphasis_UsesStylesInsteadOfInlineSemiBold() =>
        Assert.Empty(Views.SelectMany(f => XDocument.Parse(f.Text).Descendants().Attributes("FontWeight")
            .Where(a => a.Value == "SemiBold").Select(a => $"{f.Path}: {a}")));

    internal static IEnumerable<XElement> Aliases(string palette) => XDocument.Parse(SourceScan.Single($"Themes/Palette.{palette}.xaml").Text)
        .Root!.Elements().Where(e => AliasKey().IsMatch((string?)e.Attribute(_key) ?? string.Empty));

    private static IEnumerable<string> LiteralDimensions(string markup)
    {
        foreach (XElement element in XDocument.Parse(markup).Descendants())
        {
            bool icon = IsIcon(element);
            foreach (XAttribute attribute in element.Attributes())
            {
                string property = attribute.Name.LocalName;
                if ((property is "Margin" or "Padding" || (icon && property is "Width" or "Height")) && IsLiteral(attribute.Value))
                {
                    yield return attribute.ToString();
                }
            }

            string? setter = (string?)element.Attribute("Property");
            if (element.Name.LocalName == "Setter" && (setter is "Margin" or "Padding" || (icon && setter is "Width" or "Height")))
            {
                string? value = (string?)element.Attribute("Value");
                if (value is null || IsLiteral(value))
                {
                    yield return element.ToString();
                }
            }

            string[] parts = element.Name.LocalName.Split('.');
            if (parts.Length == 2 && (parts[1] is "Margin" or "Padding" || (_iconTypes.Contains(parts[0], StringComparer.Ordinal) && parts[1] is "Width" or "Height")))
            {
                if (element.HasElements || IsLiteral(element.Value))
                {
                    yield return element.ToString();
                }
            }
        }
    }

    private static bool IsLiteral(string value) => value != "0" && !value.StartsWith('{');

    private static bool IsIcon(XElement element) => _iconTypes.Contains(element.Name.LocalName, StringComparer.Ordinal)
        || element.AncestorsAndSelf().Any(e => e.Name.LocalName == "Style"
            && _iconTypes.Contains(((string?)e.Attribute("TargetType") ?? string.Empty).Replace("{x:Type ", string.Empty, StringComparison.Ordinal).TrimEnd('}'), StringComparer.Ordinal));

    [GeneratedRegex(@"\{(?:StaticResource|DynamicResource) ([\w.]+)\}")]
    private static partial Regex ResourceReference();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"^(?:Surface|Text|Border|Accent|State)\.[A-Z][A-Za-z0-9.]*$")]
    private static partial Regex AliasKey();

    [GeneratedRegex(@"new\s+Thickness\(\s*(?!0\s*\))[^)]+\)")]
    private static partial Regex NonzeroThickness();
}

