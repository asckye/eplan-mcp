// Ported from TIA Portal Openness MCP, tests/Studio/TiaOpenness.Gui.Tests/ThemeSurfaceTests.cs at commit c89b7c20.
// Copyright (c) 2026 bulaofen0036-coder. MIT License, see THIRD-PARTY-NOTICES.md.
// Changed: the pairs are the ones the Workbench draws, checked in all three palettes, graphics at 3:1; the colour scan
// covers the Workbench's files; TIA's exact-value test of the palettes is not ported (the palettes are vendored).
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench.Tests.Themes;

/// <summary>
/// Contrast and colour (workbench-ui A11Y-06, A11Y-07, COL-01, COL-04, COL-05): every text the Workbench draws reaches
/// 4.5:1 on its surface and every graphic that carries meaning 3:1, in the light, dark and high contrast palettes; the
/// palettes have the same keys; colours live only in the palettes, and high contrast only follows the system colours.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed partial class ThemeContrastTests(WpfContext wpf)
{
    /// <summary>Text on the surfaces it is drawn on: Primer's pairs and the Workbench's own (badge, segments, banner, tone).</summary>
    private static readonly (string Text, string Surface)[] _textPairs =
    [
        ("Primer.text", "Primer.bg"), ("Primer.text", "Primer.card"), ("Primer.text", "Primer.input"),
        ("Primer.textMuted", "Primer.bg"), ("Primer.textMuted", "Primer.card"), ("Primer.textMuted", "Primer.input"),
        ("Primer.text", "Primer.cardSel"), ("Primer.text", "Primer.cardSoft"), ("Primer.text", "Primer.pill"), ("Primer.text", "Primer.menuBg"),
        ("Primer.textFaint", "Primer.card"), ("Primer.textFaint", "Primer.input"),
        ("Primer.codeText", "Primer.codeBg"),
        ("Primer.accent", "Primer.bg"), ("Primer.accent", "Primer.cardSel"), ("Primer.onAccent", "Primer.accent"),
        ("Primer.warn", "Primer.bg"), ("Primer.warn", "Primer.card"), ("Primer.warn", "Primer.warnBg"), ("Primer.warn", "Primer.countWarningBg"),
        ("Primer.ok", "Primer.bg"), ("Primer.ok", "Primer.card"),
        ("Primer.diffRed", "Primer.bg"), ("Primer.diffRed", "Primer.card"), ("Primer.diffRed", "Primer.cardSel"),
        ("Primer.text", "Ant.colorErrorBg"),
    ];

    /// <summary>Graphics against what they sit on: focus ring, progress fill on its track, alert icons, selection marks.</summary>
    private static readonly (string Graphic, string Surface)[] _graphicPairs =
    [
        ("Primer.focusOuter", "Primer.bg"), ("Primer.focusOuter", "Primer.card"), ("Primer.focusOuter", "Primer.cardSel"),
        ("Ant.colorPrimary", "Ant.colorFillSecondary"), ("Primer.warn", "Ant.colorFillSecondary"),
        ("Ant.colorPrimary", "Primer.bg"), ("Primer.diffRed", "Ant.colorErrorBg"), ("Primer.warn", "Primer.bg"),
    ];

    private static readonly string[] _paletteNames = ["Light", "Dark", "HighContrast"];

    public enum Palette
    {
        Light,
        Dark,
        HighContrast,
    }

    [Theory]
    [InlineData(Palette.Light)]
    [InlineData(Palette.Dark)]
    [InlineData(Palette.HighContrast)]
    public void TextReaches4Point5_AndGraphicsReach3_OnTheirSurfaces(Palette palette)
    {
        wpf.Run(() => WithPalette(palette, () =>
        {
            Color window = Token("Primer.bg");
            var failures = new List<string>();
            foreach ((string text, string surface, double minimum) in _textPairs.Select(p => (p.Text, p.Surface, 4.5)).Concat(_graphicPairs.Select(p => (p.Graphic, p.Surface, 3.0))))
            {
                Color background = Composite(Token(surface), window);
                double contrast = Contrast(Composite(Token(text), background), background);
                if (contrast < minimum)
                {
                    failures.Add($"{palette}: {text} on {surface} = {contrast:F2}, needs {minimum}");
                }
            }

            Assert.Empty(failures);
        }));
    }

    [Fact]
    public void TheThreePalettes_HaveTheSameKeys()
    {
        string[][] keys = [.. _paletteNames.Select(name => Keys(SourceScan.Single($"Tia/Themes/Palette.{name}.xaml")))];

        Assert.Equal(keys[0], keys[1]);
        Assert.Equal(keys[0], keys[2]);
        Assert.NotEmpty(keys[0]);
    }

    [Fact]
    public void HighContrast_TakesEveryColourFromTheSystem()
    {
        XDocument palette = XDocument.Parse(SourceScan.Single("Tia/Themes/Palette.HighContrast.xaml").Text);

        Assert.All(palette.Root!.Elements(), brush => Assert.Matches(SystemColour(), (string?)brush.Attribute("Color") ?? string.Empty));
    }

    [Fact]
    public void Colours_LiveOnlyInThePalettes()
    {
        SourceFile[] files = [.. SourceScan.Markup.Concat(SourceScan.Code).Where(f => !f.Path.StartsWith("Tia/Themes/Palette.", StringComparison.Ordinal))];
        IEnumerable<string> brushes = files.Where(f => f.Path.EndsWith(".xaml", StringComparison.Ordinal))
            .SelectMany(f => XDocument.Parse(f.Text).Descendants().Where(e => e.Name.LocalName.EndsWith("Brush", StringComparison.Ordinal)).Select(e => $"{f.Path}: {e.Name.LocalName}"));
        IEnumerable<string> literals = files.SelectMany(f => LiteralColour().Matches(f.Text).Select(m => $"{f.Path}: {m.Value}"));

        // The tray icon is drawn with WinForms: it converts the palette's own tokens, it holds no colour of its own.
        Assert.Equal(["Shell/TrayIcon.cs: Color.FromArgb"], brushes.Concat(literals));
    }

    private static void WithPalette(Palette palette, Action check)
    {
        ThemeManager manager = ThemeManager.Current;
        AppTheme previous = manager.Theme;
        try
        {
            manager.Theme = palette == Palette.Dark ? AppTheme.Dark : AppTheme.Light;
            manager.HighContrastOverride = palette == Palette.HighContrast;
            check();
        }
        finally
        {
            manager.HighContrastOverride = null;
            manager.Theme = previous;
        }
    }

    private static Color Token(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    private static string[] Keys(SourceFile palette) =>
        [.. XDocument.Parse(palette.Text).Root!.Elements().Select(e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!).Order(StringComparer.Ordinal)];

    private static double Contrast(Color first, Color second)
    {
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static Color Composite(Color foreground, Color background)
    {
        double alpha = foreground.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round((foreground.R * alpha) + (background.R * (1 - alpha))),
            (byte)Math.Round((foreground.G * alpha) + (background.G * (1 - alpha))),
            (byte)Math.Round((foreground.B * alpha) + (background.B * (1 - alpha))));
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    // Transparent carries no hue: it lets the palette show through.
    [GeneratedRegex(@"#[\da-fA-F]{3,8}\b|\b(?:Colors|Brushes)\.(?!Transparent\b)\w+|\bColor\.From\w+")]
    private static partial Regex LiteralColour();

    [GeneratedRegex(@"^\{DynamicResource \{x:Static SystemColors\.\w+ColorKey\}\}$")]
    private static partial Regex SystemColour();
}
