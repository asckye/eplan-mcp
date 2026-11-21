using System.Net;
using System.Windows;
using System.Windows.Controls;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Settings;

/// <summary>ADR-0066 on the settings page: nothing goes online until the click; a newer version offers its release page only.</summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class UpdateCheckPageTests(WpfContext wpf)
{
    [Fact]
    public void OpeningSettings_DoesNotGoOnline_AndTheClickShowsTheNewerVersionWithItsReleasePage()
    {
        using var handler = new UpdateCheckTests.FakeHandler(HttpStatusCode.OK, """[{"tag_name":"v99.0.0","html_url":"https://github.com/asckye/eplan-mcp/releases/tag/v99.0.0"}]""");
        var shell = new RecordingShell();
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            window.OpenSettings();
            var page = (SettingsPage)window.FindName("SettingsPage");
            page.UpdateHandler = handler;
            page.Attach(new StartAtLogin(@"Software\EplanMcp.Tests\Unused", "unused.exe"), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json"), shell: shell);
            page.OpenStorageCategory();
            WpfContext.Drain();
            Assert.Empty(handler.Requests);

            page.CheckUpdateAsync().GetAwaiter().GetResult();
            WpfContext.Drain();

            Assert.Single(handler.Requests);
            Assert.True(((FrameworkElement)page.FindName("UpdateNewer")).IsVisible);
            Assert.Equal(Loc.Current.T("Settings.UpdateNewer", "v99.0.0"), ((TextBlock)page.FindName("UpdateNewerText")).Text);
            Click((UIElement)page.FindName("OpenUpdate"));
            Assert.Equal(["https://github.com/asckye/eplan-mcp/releases/tag/v99.0.0"], shell.Opened);
        }));
    }

    [Fact]
    public void NoNetwork_SaysSo_AndTheButtonWorksAgain()
    {
        using var handler = new UpdateCheckTests.FakeHandler(HttpStatusCode.OK, "[]") { Throw = new System.Net.Http.HttpRequestException("offline") };
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            window.OpenSettings();
            var page = (SettingsPage)window.FindName("SettingsPage");
            page.UpdateHandler = handler;
            page.OpenStorageCategory();

            page.CheckUpdateAsync().GetAwaiter().GetResult();
            WpfContext.Drain();

            Assert.Equal(Loc.Current.T("Settings.UpdateFailed", "offline"), ((TextBlock)page.FindName("UpdateText")).Text);
            Assert.False(((FrameworkElement)page.FindName("UpdateNewer")).IsVisible);
            Assert.True(((UIElement)page.FindName("UpdateButton")).IsEnabled);
        }));
    }

    private sealed class RecordingShell : IShellOpen
    {
        public List<string> Opened { get; } = [];

        public bool Open(string path)
        {
            Opened.Add(path);
            return true;
        }
    }
}
