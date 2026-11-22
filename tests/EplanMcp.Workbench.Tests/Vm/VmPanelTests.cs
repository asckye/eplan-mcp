using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EplanMcp.Workbench.Mcp;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Vm;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Vm;

[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class VmPanelTests(WpfContext wpf)
{
    private static readonly string[] _inputs = ["Address", "Port", "FirewallValue"];
    private static readonly string[] _commands = ["Allow", "Remove", "Refresh", "Copy", "Start", "Stop"];
    private static readonly string[] _locked = ["Address", "Port", "Start"];
    private static readonly string[] _available = ["Allow", "Remove", "Refresh", "Copy"];

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Page_InBothLanguages_HasNamesLabelsAndDisabledReasons_WithoutSharedKey(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm();
            VmView view = fake.Create();
            VmPanel panel = Attach(window, view);
            Assert.Null(panel.FindName("KeyValue"));
            Assert.Equal("8766", ((TextBox)panel.FindName("Port")).Text);
            foreach (string name in _inputs)
            {
                var input = (FrameworkElement)panel.FindName(name);
                Assert.NotNull(AutomationProperties.GetLabeledBy(input));
                Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(input)!.GetName()), name);
            }

            // ADR-0067: no host address and no EPLAN instance; the MCP address is shown, selectable, with a copy button.
            Assert.Null(panel.FindName("HostAddress"));
            Assert.Null(panel.FindName("Instance"));
            Assert.Equal("http://192.168.56.2:8766/mcp", ((TextBox)panel.FindName("Endpoint")).Text);
            var allow = (Button)panel.FindName("Allow");
            Assert.True(allow.IsEnabled);
            Assert.Equal(Loc.Current["Vm.Uac"], allow.ToolTip);
            ((TextBox)panel.FindName("Port")).Text = "65536";
            Assert.False(allow.IsEnabled);
            Assert.Equal(Loc.Current["Vm.PortError"], AutomationProperties.GetHelpText(allow));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((TextBlock)panel.FindName("Status")));
            Assert.Single(fake.Host.Runs);
            Assert.Empty(fake.Elevated);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Page_UacCancellationAndRemoval_RefreshWithFakeRunnersOnly(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm { ExitCode = null };
            VmPanel panel = Attach(window, fake.Create());
            Click((Button)panel.FindName("Allow"));
            Assert.Equal(Loc.Current["Vm.Cancelled"], ((TextBlock)panel.FindName("Status")).Text);
            fake.ExitCode = 0;
            Click((Button)panel.FindName("Remove"));
            Click((Button)panel.FindName("Refresh"));
            Assert.Equal([false, true], fake.Elevated.Select(r => r.Remove));
            Assert.Equal(Loc.Current["Vm.Absent"], ((TextBox)panel.FindName("FirewallValue")).Text);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Page_MissingHostDisablesEveryCommandWithAReason_AndLanguageChangesImmediately(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm();
            VmPanel panel = Attach(window, fake.Create(available: false));
            foreach (string name in _commands)
            {
                var button = (ButtonBase)panel.FindName(name);
                Assert.False(button.IsEnabled);
                Assert.Equal(Loc.Current["Host.Missing"], AutomationProperties.GetHelpText(button));
            }

            Loc.Current.Language = language == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese;
            WpfContext.Drain();
            Assert.Equal(Loc.Current["Vm.Allow"], ((Button)panel.FindName("Allow")).Content);
            Assert.Equal(Loc.Current["Host.Missing"], AutomationProperties.GetHelpText((Button)panel.FindName("Allow")));
            Assert.Empty(fake.Host.Runs);
            Assert.Empty(fake.Elevated);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Service_StartingListeningStopped_ExplainsLockedSettingsAndKeepsNetworkCommands(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm();
            var launcher = new FakeVmLauncher();
            using VmView view = fake.Create(launcher: launcher);
            VmPanel panel = Attach(window, view);
            var start = (Button)panel.FindName("Start");
            var stop = (Button)panel.FindName("Stop");
            var status = (TextBlock)panel.FindName("ServiceStatus");
            Assert.Equal(Loc.Current["Vm.Start"], start.Content);
            Assert.True(start.IsEnabled);
            Assert.False(stop.IsEnabled);
            Assert.Equal(Loc.Current["Vm.Stopped"], status.Text);
            Assert.Empty(launcher.Starts);
            Click(start);
            Assert.Equal(Loc.Current["Vm.Starting"], status.Text);
            Assert.True(stop.IsEnabled);
            foreach (string name in _locked)
            {
                var field = (Control)panel.FindName(name);
                Assert.False(field.IsEnabled);
                Assert.Equal(Loc.Current["Vm.StopFirst"], AutomationProperties.GetHelpText(field));
            }

            foreach (string name in _available)
            {
                Assert.True(((Control)panel.FindName(name)).IsEnabled);
            }

            launcher.Child.Listening.SetResult(new VmEndpoint(view.Address, 8766));
            WpfContext.DrainUntil(() => !view.Starting, "The listening signal did not reach the page.");
            Assert.Contains("http://192.168.56.2:8766/mcp", status.Text, StringComparison.Ordinal);
            Click(stop);
            WpfContext.DrainUntil(() => !view.Running, "The stopped child did not reach the page.");
            Assert.True(launcher.Child.Stopped);
            Assert.Equal(Loc.Current["Vm.Stopped"], status.Text);
            Assert.True(((Control)panel.FindName("Port")).IsEnabled);
            Assert.Single(launcher.Starts);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void ServiceFailure_ShowsPlainHostErrorWithAccessibleStatus(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm();
            var launcher = new FakeVmLauncher();
            using VmView view = fake.Create(launcher: launcher);
            VmPanel panel = Attach(window, view);
            Click((Button)panel.FindName("Start"));
            launcher.Child.Exit.SetResult("Address already in use");
            WpfContext.DrainUntil(() => !view.Running, "The failed child did not reach the page.");
            var status = (TextBlock)panel.FindName("ServiceStatus");
            Assert.Contains(Loc.Current["Vm.Failed"], status.Text, StringComparison.Ordinal);
            Assert.Contains("Address already in use", status.Text, StringComparison.Ordinal);
            Assert.True(((Button)panel.FindName("Start")).IsEnabled);
        }));

    private static VmPanel Attach(MainWindow window, VmView view)
    {
        window.Width = 960;
        window.AttachMcp(view);
        window.Navigate(WorkbenchPage.Mcp, NavigationOrigin.Menu);
        var mcp = (McpPage)window.FindName("McpPage");
        ((RadioButton)mcp.FindName("HttpChoice")).IsChecked = true;
        WpfContext.Drain();
        return (VmPanel)mcp.FindName("VmConnection");
    }
}
