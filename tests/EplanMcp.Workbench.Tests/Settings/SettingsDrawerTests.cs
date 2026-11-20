using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Themes;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Settings;

/// <summary>
/// The dedicated Settings page: navigated from the bottom of the left Activity Rail (Ctrl+,),
/// switching Language, Light/Dark theme, Dual Brand Accent (Cobalt / Petrol), and Normal-Write Manual Approval at once.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class SettingsDrawerTests(WpfContext wpf)
{
    [Fact]
    public void OpeningSettings_ShowsDedicatedSettingsPage_AndFocusesItsHeading()
    {
        wpf.Run(() => With(window =>
        {
            window.OpenSettings();
            WpfContext.Drain();

            var page = (SettingsPage)window.FindName("SettingsPage");
            Assert.Equal(WorkbenchPage.Settings, window.CurrentPage);
            Assert.Equal(Visibility.Visible, page.Visibility);
            Assert.Same(page.Heading, Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void ChoosingLanguageThemeAccentOrApproval_AppliesAtOnce()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            AppTheme theme = ThemeManager.Current.Theme;
            BrandAccent accent = AccentManager.Current.Accent;
            using var temp = new TempDir();
            string config = Path.Combine(temp.Path, "config.json");
            var policy = new SafetyPolicyStore(config);
            policy.Load(fallbackRequireApproval: true);
            window.AttachPolicy(policy);
            window.AttachSettings(new StartAtLogin(@"Software\EplanMcp.Tests\Unused", "unused.exe"), config, policy);
            try
            {
                window.OpenSettings();
                var page = (SettingsPage)window.FindName("SettingsPage");

                // Safety: toggle normal-write approval off -> config.json updated to writesOutsideScratch=allow
                var apSwitch = (ToggleButton)page.FindName("ApprovalSwitch");
                Assert.True(apSwitch.IsChecked);
                apSwitch.IsChecked = false;
                WpfContext.Drain();
                Assert.False(policy.RequireNormalWriteApproval);
                Assert.Contains("\"writesOutsideScratch\": \"allow\"", File.ReadAllText(config), StringComparison.Ordinal);

                // Appearance: Light / Dark + Dual Brand Accent (Cobalt / Petrol) + Language
                Choose((RadioButton)page.FindName("NavAppearance"));
                RadioButton[] segments = [.. Descendants<RadioButton>(page)];

                Choose(segments.Single(r => AutomationProperties.GetName(r) == "English"));
                Assert.Equal(AppLanguage.English, Loc.Current.Language);
                Assert.Equal("Settings – EplanMcp Workbench", window.Title);

                Choose(segments.Single(r => AutomationProperties.GetName(r) == "Dark"));
                Assert.Equal(AppTheme.Dark, ThemeManager.Current.Theme);

                Choose((RadioButton)page.FindName("AccentPetrolRadio"));
                Assert.Equal(BrandAccent.Petrol, AccentManager.Current.Accent);
            }
            finally
            {
                AccentManager.Current.Accent = accent;
                ThemeManager.Current.Theme = theme;
            }
        }));
    }

    private static void Choose(RadioButton segment)
    {
        segment.IsChecked = true;
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

    private sealed class TempDir : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("eplanmcp-settings-test-");

        public string Path => _dir.FullName;

        public void Dispose() => _dir.Delete(recursive: true);
    }
}
