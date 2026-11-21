using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EplanMcp.Workbench.Shell;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The font tokens: Windows' own Segoe UI Variable and Cascadia Mono as the Workbench 4.0 design names them, Microsoft YaHei UI for Chinese (ADR-0034 decision 3), and the ligatures (TYP-11).
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class WorkbenchFontsTests(WpfContext wpf)
{
    [Fact]
    public void TheTokensNameWindowsOwnFonts_WithMicrosoftYaHeiUiForChinese()
    {
        wpf.Run(() =>
        {
            var resources = new ResourceDictionary();

            WorkbenchFonts.Apply(resources);

            Assert.Equal("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI", ((FontFamily)resources["Ui.Font"]).Source);
            Assert.Equal("Cascadia Mono, Consolas, Microsoft YaHei UI", ((FontFamily)resources["Ui.FontMono"]).Source);
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
