using System.Windows;
using System.Windows.Controls;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Tests.Approvals;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The title bar and the status bar name the project and the EPLAN of the last question (the 2026-11-20 design); before
/// any question the title bar names the page, and no environment shows.
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
            Assert.Equal("/ 协作与审批", ((TextBlock)window.FindName("CurrentPageText")).Text);
            Assert.False(((FrameworkElement)window.FindName("EnvPill")).IsVisible);
            Assert.False(((FrameworkElement)window.FindName("EnvStatusText")).IsVisible);
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

            Assert.Equal("/ Line1", ((TextBlock)window.FindName("CurrentPageText")).Text);
            Assert.True(((FrameworkElement)window.FindName("EnvPill")).IsVisible);
            Assert.Equal("EPLAN 2026.0.3.25421 · Pro Panel", ((TextBlock)window.FindName("EnvText")).Text);
            Assert.Equal("EPLAN 2026.0.3.25421 · Pro Panel  Line1", ((TextBlock)window.FindName("EnvStatusText")).Text);
            Assert.StartsWith("协作与审批", window.Title, StringComparison.Ordinal); // the window title still names the page
        }));
    }
}
