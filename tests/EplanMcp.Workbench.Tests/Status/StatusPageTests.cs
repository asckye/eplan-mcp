using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Status;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Status;

/// <summary>
/// The status page in a real window (M5 design 4.4 item 4, NAV-05, STS-02, STS-03, STS-06, ERR-04, ERR-07): doctor runs
/// each time the page opens and on F5, never on its own; its groups are named cards; a suspension and a failure each have
/// their Banner.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class StatusPageTests(WpfContext wpf)
{
    private const string Suspended = """
        {"schemaVersion":1,"hostVersion":"0.1.0","status":"warning","checks":[
          {"id":"suspension","status":"warning","zh":"写入已暂停：Line1","en":"Writes suspended: Line1"},
          {"id":"bridge.licence","status":"ok","zh":"已授权","en":"Licensed"}
        ]}
        """;

    private readonly FakeHost _host = new();

    [Fact]
    public void OpeningThePage_RunsDoctorOnce_AndShowsItsGroupsAsNamedCards_SeverityInWords()
    {
        _host.Answer = _ => new HostRun(1, Suspended);
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            Assert.Empty(_host.Runs); // not before the page is opened

            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Shortcut);
            WpfContext.Drain();

            Assert.Single(_host.Runs);
            Assert.Equal("eplanmcp 0.1.0：有检查给出警告", ((TextBlock)page.FindName("Caption")).Text);
            GroupBox[] cards = [.. Descendants<GroupBox>(page)];
            Assert.Equal(["授权", "写入暂停", "下一次审批"], cards.Select(AutomationProperties.GetName));
            Assert.Contains(Descendants<TextBlock>(cards[0]), t => t.Text == "正常");
            Assert.True(((FrameworkElement)page.FindName("SuspensionBanner")).IsVisible);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((TextBlock)page.FindName("Caption")));
            Assert.Same(page.FindName("PageTitle"), System.Windows.Input.Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void F5_AndOpeningAgain_RunDoctorAgain()
    {
        _host.Answer = _ => new HostRun(0, Snapshots.Read("doctor-json"));
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Menu);
            WpfContext.Drain();
            MainWindow.RefreshCommand.Execute(null, window);
            WpfContext.Drain();
            window.Navigate(WorkbenchPage.Approvals, NavigationOrigin.Menu);
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Menu);
            WpfContext.Drain();

            Assert.Equal(3, _host.Runs.Count);
        }));
    }

    [Fact]
    public void WhileDoctorRuns_TheProgressShows_AndRefreshWaits()
    {
        TaskCompletionSource<HostRun> held = _host.Hold();
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Menu);
            WpfContext.Drain();

            Assert.True(((FrameworkElement)page.FindName("Working")).IsVisible);
            Assert.False(((Button)page.FindName("RefreshButton")).IsEnabled);
            held.SetResult(new HostRun(0, Snapshots.Read("doctor-json")));
            Until(() => !((FrameworkElement)page.FindName("Working")).IsVisible);
            Assert.True(((Button)page.FindName("RefreshButton")).IsEnabled);
        }));
    }

    [Fact]
    public void ADoctorThatGivesNoResult_ShowsTheErrorBanner_WithTheCommandLine()
    {
        _host.Answer = _ => throw new HostCommandException(HostFailure.TimedOut, "eplanmcp did not finish within 60 s.");
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Menu);
            WpfContext.Drain();

            Assert.StartsWith("错误：eplanmcp.exe 没有按时完成", ((TextBlock)page.FindName("ErrorText")).Text, StringComparison.Ordinal);
            Assert.StartsWith(_host.Executable + " doctor --format json", ((TextBox)page.FindName("ErrorDetails")).Text, StringComparison.Ordinal);
            Assert.Empty(Descendants<GroupBox>(page));
        }));
    }

    [Fact]
    public void WithoutAHost_NothingRuns_AndThePageSaysWhy()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage(
            (window, page) =>
            {
                window.Navigate(WorkbenchPage.Status, NavigationOrigin.Menu);
                WpfContext.Drain();

                Assert.Equal("Workbench 旁边没有 eplanmcp.exe。请重新安装 EplanMcp。", ((TextBlock)page.FindName("Caption")).Text);
                Assert.False(((Button)page.FindName("RefreshButton")).IsEnabled);
                Assert.NotEmpty(AutomationProperties.GetHelpText((Button)page.FindName("RefreshButton")));
            },
            new HostCommands(null)));
        Assert.Empty(_host.Runs);
    }

    [Fact]
    public void EveryCardAndLine_HasAName_ForScreenReaders()
    {
        _host.Answer = _ => new HostRun(1, Suspended);
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Menu);
            WpfContext.Drain();
            var peers = new List<AutomationPeer>();
            void Walk(AutomationPeer peer)
            {
                peers.Add(peer);
                foreach (AutomationPeer child in peer.GetChildren() ?? [])
                {
                    Walk(child);
                }
            }

            Walk(UIElementAutomationPeer.CreatePeerForElement(page));
            Assert.DoesNotContain(peers, p => p.GetAutomationControlType() is AutomationControlType.Group or AutomationControlType.Button && string.IsNullOrWhiteSpace(p.GetName()));
        }));
    }

    private void WithPage(Action<MainWindow, StatusPage> test, HostCommands? host = null) => With(window =>
    {
        window.AttachStatus(new StatusView(host ?? new HostCommands(_host), () => true));
        test(window, (StatusPage)window.FindName("StatusPage"));
    });

    private static void Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Not in time.");
            WpfContext.Drain();
            Thread.Sleep(10);
        }
    }

    private static List<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                found.Add(match);
            }

            found.AddRange(Descendants<T>(child));
        }

        return found;
    }
}
