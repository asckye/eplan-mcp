using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Settings;

/// <summary>
/// The settings drawer (workbench-ui DLG-10, LOC-08, COL-09): a modal pane with its title focused, Tab kept inside, Esc
/// and the scrim closing it with focus back on the opener; language and theme apply at once.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class SettingsDrawerTests(WpfContext wpf)
{
    [Fact]
    public void Opening_FocusesTheDrawersTitle_AndEscGivesTheFocusBack()
    {
        wpf.Run(() => With(window =>
        {
            var opener = (Button)window.FindName("SettingsButton");
            opener.Focus();

            Click(opener);
            var drawer = (SettingsDrawer)window.FindName("SettingsPane");
            Border pane = Pane(drawer);
            Assert.Equal(Visibility.Visible, Layer(window).Visibility);
            Assert.Same(drawer.FindName("Heading"), Keyboard.FocusedElement);
            Assert.True(AutomationProperties.GetIsDialog(pane));
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(pane));

            Escape(drawer);

            Assert.Equal(Visibility.Collapsed, Layer(window).Visibility);
            Assert.Same(opener, Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void WhileOpen_ThePageShortcutsDoNothing()
    {
        wpf.Run(() => With(window =>
        {
            window.OpenSettings();

            Assert.False(MainWindow.NavigateCommand.CanExecute(WorkbenchPage.Audit, window));
            window.CloseSettings();
            Assert.True(MainWindow.NavigateCommand.CanExecute(WorkbenchPage.Audit, window));
        }));
    }

    [Fact]
    public void ChoosingALanguageOrATheme_AppliesAtOnce()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            AppTheme theme = ThemeManager.Current.Theme;
            try
            {
                window.OpenSettings();
                var drawer = (SettingsDrawer)window.FindName("SettingsPane");
                RadioButton[] segments = [.. Descendants<RadioButton>(drawer)];

                Choose(segments.Single(r => AutomationProperties.GetName(r) == "English"));
                Assert.Equal(AppLanguage.English, Loc.Current.Language);
                Assert.Equal("Approvals – EplanMcp Workbench", window.Title);
                Choose(segments.Single(r => AutomationProperties.GetName(r) == "Dark"));
                Assert.True(ThemeManager.Current.EffectivelyDark || ThemeManager.Current.EffectivelyHighContrast);
                Assert.Equal(AppTheme.Dark, ThemeManager.Current.Theme);
            }
            finally
            {
                ThemeManager.Current.Theme = theme;
            }
        }));
    }

    private static Grid Layer(MainWindow window) => (Grid)window.FindName("SettingsLayer");

    private static Border Pane(SettingsDrawer drawer) => (Border)drawer.Content;

    private static void Choose(RadioButton segment)
    {
        segment.IsChecked = true;
        WpfContext.Drain();
    }

    private static void Escape(UIElement element)
    {
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        WpfContext.Drain();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }
}
