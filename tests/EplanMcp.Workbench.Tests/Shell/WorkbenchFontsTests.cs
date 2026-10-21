using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EplanMcp.Workbench.Shell;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The font tokens (workbench-ui TYP-01) and the ligatures (TYP-11). Maple Mono CN itself is not in the repository or
/// on CI; with it the tokens name it first (checked by hand, see the PR), without it they name the fallbacks.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class WorkbenchFontsTests(WpfContext wpf) : IDisposable
{
    private readonly DirectoryInfo _app = Directory.CreateTempSubdirectory("eplanmcp-workbench-fonts-");

    public void Dispose() => _app.Delete(recursive: true);

    [Fact]
    public void WithoutAFontsFolder_TheTokensNameTheSystemFallbacks_WithMicrosoftYaHeiUi()
    {
        wpf.Run(() =>
        {
            var resources = new ResourceDictionary();

            Assert.False(WorkbenchFonts.Apply(resources, _app.FullName));
            Assert.Equal("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI", ((FontFamily)resources["Ui.Font"]).Source);
            Assert.Equal("Cascadia Mono, Consolas, Microsoft YaHei UI", ((FontFamily)resources["Ui.FontMono"]).Source);
        });
    }

    [Fact]
    public void AFontsFolderWithoutMapleMono_IsNotTakenForIt()
    {
        Directory.CreateDirectory(Path.Combine(_app.FullName, "Fonts"));
        File.WriteAllText(Path.Combine(_app.FullName, "Fonts", "MapleMono-CN-Regular.ttf"), "not a font");

        wpf.Run(() =>
        {
            var resources = new ResourceDictionary();

            Assert.False(WorkbenchFonts.Apply(resources, _app.FullName));
            Assert.Null(((FontFamily)resources["Ui.Font"]).BaseUri);
        });
    }

    [Fact]
    public void TheApplication_OverridesTheVendoredThemesFonts()
    {
        wpf.Run(() =>
        {
            WorkbenchFonts.Apply(Application.Current.Resources, _app.FullName);

            Assert.Equal(WorkbenchFonts.UiFallback, ((FontFamily)Application.Current.FindResource("Ui.Font")).Source);
            Assert.Equal(WorkbenchFonts.MonoFallback, ((FontFamily)Application.Current.FindResource("Ui.FontMono")).Source);
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
