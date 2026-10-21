using System.Windows;
using System.Windows.Media;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench.Tests.Themes;

/// <summary>
/// Light, dark and high contrast (workbench-ui COL-02, COL-09): the vendored ThemeManager swaps the palette in the
/// application's resources in place, and high contrast wins over the chosen theme.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ThemeSwitchTests(WpfContext wpf)
{
    [Fact]
    public void ChoosingATheme_SwapsThePaletteWithoutRebuildingAnything()
    {
        wpf.Run(() =>
        {
            ThemeManager manager = ThemeManager.Current;
            AppTheme previous = manager.Theme;
            ResourceDictionary resources = Application.Current.Resources;
            int dictionaries = resources.MergedDictionaries.Count;
            try
            {
                manager.Theme = AppTheme.Dark;
                Assert.Equal(Color.FromRgb(0x00, 0x00, 0x00), Token("Primer.bg"));
                manager.Theme = AppTheme.Light;
                Assert.Equal(Color.FromRgb(0xF5, 0xF5, 0xF5), Token("Primer.bg"));
                Assert.Equal(dictionaries, resources.MergedDictionaries.Count);
            }
            finally
            {
                manager.Theme = previous;
            }
        });
    }

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void HighContrast_UsesTheSystemColoursWhicheverThemeIsChosen(AppTheme chosen)
    {
        wpf.Run(() =>
        {
            ThemeManager manager = ThemeManager.Current;
            AppTheme previous = manager.Theme;
            try
            {
                manager.Theme = chosen;
                manager.HighContrastOverride = true;

                Assert.Equal(SystemColors.WindowColor, Token("Primer.bg"));
                Assert.Equal(SystemColors.WindowTextColor, Token("Primer.textMuted"));
            }
            finally
            {
                manager.HighContrastOverride = null;
                manager.Theme = previous;
            }
        });
    }

    private static Color Token(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
}
