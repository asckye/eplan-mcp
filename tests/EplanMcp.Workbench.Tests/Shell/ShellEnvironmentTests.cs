using System.Windows;
using System.Windows.Controls;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Tests.Approvals;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The title bar and the status bar name the project and the EPLAN of the last question (the Workbench 4.0 design); before
/// any question the status bar says no request has come yet.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ShellEnvironmentTests(WpfContext wpf)
{
    [Fact]
    public void BeforeAQuestion_ThePageIsNamed_AndNoEnvironmentShows()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            Assert.Equal("AI 协作与审批", ((TextBlock)window.FindName("CurrentPageText")).Text);
            Assert.Equal("EPLAN", ((TextBlock)window.FindName("EnvStatusText")).Text);
            Assert.Equal("尚无请求", ((TextBlock)window.FindName("ProjectStatusText")).Text);
            Assert.False(((FrameworkElement)window.FindName("ClientStatusText")).IsVisible);
        }));
    }

    [Fact]
    public void AQuestion_NamesItsProjectAndEplan_InTheTitleBarAndTheStatusBar()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            var clock = new ManualTime(Asks.Now);
            var queue = new ApprovalQueue(clock);
            using var view = new ApprovalQueueView(queue, clock);
            view.Follow(window.Dispatcher, () => window.ShowQueue(queue, listening: true));
            window.Attach(view);

            queue.Add(Asks.Write("r1"));
            WpfContext.Drain();

            Assert.Equal("AI 协作与审批", ((TextBlock)window.FindName("CurrentPageText")).Text);
            Assert.Equal("EPLAN 2026.0.3.25421 · Pro Panel", ((TextBlock)window.FindName("EnvStatusText")).Text);
            Assert.Equal("Line1", ((TextBlock)window.FindName("ProjectStatusText")).Text);
            Assert.EndsWith(" · PID 4242", ((TextBlock)window.FindName("ClientStatusText")).Text, StringComparison.Ordinal);
            Assert.StartsWith("协作与审批", window.Title, StringComparison.Ordinal); // the window title still names the page

            window.Navigate(WorkbenchPage.Project, NavigationOrigin.Menu);
            Assert.Equal("项目 · Line1", ((TextBlock)window.FindName("CurrentPageText")).Text);
        }));
    }
}
