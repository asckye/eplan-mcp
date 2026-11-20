using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EplanMcp.Workbench.Shell;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The font tokens: the design's Inter and JetBrains Mono from the assembly, Microsoft YaHei UI for Chinese (ADR-0034 decision 3 as revised 2026-11-20), and the ligatures (TYP-11).
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class WorkbenchFontsTests(WpfContext wpf)
{
    [Fact]
    public void TheTokensNameTheBundledFonts_WithMicrosoftYaHeiUiForChinese()
    {
        wpf.Run(() =>
        {
            var resources = new ResourceDictionary();

            WorkbenchFonts.Apply(resources);

            Assert.Equal("./Fonts/#Inter, Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI", ((FontFamily)resources["Ui.Font"]).Source);
            Assert.Equal("./Fonts/#JetBrains Mono, Cascadia Mono, Consolas, Microsoft YaHei UI", ((FontFamily)resources["Ui.FontMono"]).Source);
            Assert.Equal(WorkbenchFonts.BaseUri, ((FontFamily)resources["Ui.Font"]).BaseUri);
        });
    }

    [Theory]
    [InlineData("Ui.Font", "Inter")]
    [InlineData("Ui.FontMono", "JetBrains Mono")]
    public void TheBundledFonts_AreFoundInTheAssembly(string token, string family)
    {
        wpf.Run(() =>
        {
            var resources = new ResourceDictionary();
            WorkbenchFonts.Apply(resources);

            // The first family resolves to a typeface from the assembly, not to a fallback.
            Typeface face = ((FontFamily)resources[token]).GetTypefaces().First();
            Assert.True(face.TryGetGlyphTypeface(out GlyphTypeface glyphs));
            Assert.Equal(family, glyphs.FamilyNames[new System.Globalization.CultureInfo("en-US")]);
        });
    }

    [Fact]
    public void TheApplication_OverridesTheVendoredThemesFonts()
    {
        wpf.Run(() =>
        {
            WorkbenchFonts.Apply(Application.Current.Resources);

            Assert.Equal(WorkbenchFonts.Ui, ((FontFamily)Application.Current.FindResource("Ui.Font")).Source);
            Assert.Equal(WorkbenchFonts.Mono, ((FontFamily)Application.Current.FindResource("Ui.FontMono")).Source);
        });
    }

    [Fact]
    public void TheWindowStyle_TurnsLigaturesOffForEveryTextInside()
    {
        wpf.Run(() =>
        {
            var text = new TextBlock { Text = "-> != ==" };
            var box = new TextBox { Text = "-> != ==" };
            var window = new Window { Content = new StackPanel { Children = { text, box } } };
            window.SetResourceReference(FrameworkElement.StyleProperty, "Workbench.Window");

            Assert.All(new DependencyObject[] { text, box }, element =>
            {
                Assert.False(Typography.GetStandardLigatures(element));
                Assert.False(Typography.GetContextualLigatures(element));
                Assert.False(Typography.GetContextualAlternates(element));
            });
        });
    }
}
