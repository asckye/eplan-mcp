using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Vm;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Vm;

[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class VmPanelTests(WpfContext wpf)
{
    private static readonly string[] _inputs = ["Address", "Port", "HostAddress", "KeyValue", "FirewallValue", "Instance"];
    private static readonly string[] _commands = ["Generate", "Reveal", "Copy", "Delete", "Allow", "Remove", "Refresh", "Instances", "Start", "Stop"];
    private static readonly string[] _locked = ["Address", "Port", "Instance", "Generate", "Delete", "Instances", "Start"];
    private static readonly string[] _available = ["HostAddress", "Allow", "Remove", "Refresh", "Reveal", "Copy"];
    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Page_InBothLanguages_HasNamesLabelsMaskedKeyAndDisabledReasons(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm { StoredKey = FakeVm.Key };
            VmView view = fake.Create();
            view.HostAddress = string.Empty;
            VmPanel panel = Attach(window, view);
            Assert.Equal(Loc.Current["Vm.Title"], Descendants<TextBlock>(panel).First().Text);
            Assert.Equal(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(Descendants<TextBlock>(panel).First()));
            Assert.Equal("••••••••", ((TextBox)panel.FindName("KeyValue")).Text);
            Assert.Equal("8766", ((TextBox)panel.FindName("Port")).Text);
            foreach (string name in _inputs)
            {
                var input = (FrameworkElement)panel.FindName(name);
                Assert.NotNull(AutomationProperties.GetLabeledBy(input));
                Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(input)!.GetName()), name);
            }

            foreach (ButtonBase button in Descendants<ButtonBase>(panel))
            {
                Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(button)!.GetName()));
                Assert.DoesNotContain(FakeVm.Key, AutomationProperties.GetName(button), StringComparison.Ordinal);
                Assert.DoesNotContain(FakeVm.Key, AutomationProperties.GetHelpText(button), StringComparison.Ordinal);
            }

            var allow = (Button)panel.FindName("Allow");
            Assert.False(allow.IsEnabled);
            Assert.Equal(Loc.Current["Vm.HostError"], AutomationProperties.GetHelpText(allow));
            ((TextBox)panel.FindName("HostAddress")).Text = "192.168.56.1";
            WpfContext.Drain();
            Assert.True(allow.IsEnabled);
            Assert.Equal(Loc.Current["Vm.Uac"], allow.ToolTip);
            ((TextBox)panel.FindName("Port")).Text = "65536";
            Assert.False(allow.IsEnabled);
            Assert.Equal(Loc.Current["Vm.PortError"], AutomationProperties.GetHelpText(allow));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((TextBlock)panel.FindName("Status")));
            Assert.Equal(2, fake.Host.Runs.Count);
            Assert.Empty(fake.Elevated);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Page_KeyCommandsRequireConfirmation_ClipboardOnlyOnExplicitCopy(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm { StoredKey = FakeVm.Key };
            VmPanel panel = Attach(window, fake.Create());
            var questions = new List<string>();
            bool confirmed = false;
            panel.Confirm = kind => { questions.Add(kind); return confirmed; };
            panel.CopyText = fake.Clipboard.Add;
            Click((Button)panel.FindName("Generate"));
            Click((Button)panel.FindName("Delete"));
            Assert.Equal(["Regenerate", "Delete"], questions);
            Assert.Equal(2, fake.Host.Runs.Count);
            Assert.Empty(fake.Clipboard);
            Click((ToggleButton)panel.FindName("Reveal"));
            Assert.Equal(FakeVm.Key, ((TextBox)panel.FindName("KeyValue")).Text);
            Assert.True(((ToggleButton)panel.FindName("Reveal")).IsChecked);
            Click((Button)panel.FindName("Copy"));
            Assert.Equal([FakeVm.Key], fake.Clipboard);
            Assert.DoesNotContain(FakeVm.Key, ((TextBlock)panel.FindName("Status")).Text, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeVm.Key, AutomationProperties.GetName((TextBox)panel.FindName("KeyValue")), StringComparison.Ordinal);
            confirmed = true;
            Click((Button)panel.FindName("Generate"));
            Assert.Equal("••••••••", ((TextBox)panel.FindName("KeyValue")).Text);
            Click((Button)panel.FindName("Delete"));
            Assert.False(((Button)panel.FindName("Copy")).IsEnabled);
            Assert.Equal(Loc.Current["Vm.NoKey"], AutomationProperties.GetHelpText((Button)panel.FindName("Copy")));
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
            var fake = new FakeVm { StoredKey = FakeVm.Key };
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
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
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
            Loc.Current.Language = language == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese;
            WpfContext.Drain();
            Assert.Equal(Loc.Current["Vm.Start"], start.Content);
            Assert.Equal(Loc.Current["Vm.StopFirst"], AutomationProperties.GetHelpText(start));
            Assert.Equal(view.ServiceStatus, status.Text);
            Click(stop);
            WpfContext.DrainUntil(() => !view.Running, "The stopped child did not reach the page.");
            Assert.True(launcher.Child.Stopped);
            Assert.Equal(Loc.Current["Vm.Stopped"], status.Text);
            Assert.True(((Control)panel.FindName("Port")).IsEnabled);
            Assert.True(((Button)panel.FindName("Generate")).IsEnabled);
            Assert.Single(launcher.Starts);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese, false, "[100]", "Vm.NoKey")]
    [InlineData(AppLanguage.English, false, "[100]", "Vm.NoKey")]
    [InlineData(AppLanguage.Chinese, true, "[]", "Vm.NoEplan")]
    [InlineData(AppLanguage.English, true, "[]", "Vm.NoEplan")]
    [InlineData(AppLanguage.Chinese, true, "[100,200]", "Vm.PickInstance")]
    [InlineData(AppLanguage.English, true, "[100,200]", "Vm.PickInstance")]
    public void Service_PrerequisitesAndExplicitInstanceSelection_HaveLocalizedReasons(AppLanguage language, bool key, string processes, string reason) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm { StoredKey = key ? FakeVm.Key : null, Instances = "{\"schemaVersion\":1,\"processes\":" + processes + "}" };
            var launcher = new FakeVmLauncher();
            using VmView view = fake.Create(launcher: launcher);
            VmPanel panel = Attach(window, view);
            var start = (Button)panel.FindName("Start");
            Assert.False(start.IsEnabled);
            Assert.Equal(Loc.Current[reason], AutomationProperties.GetHelpText(start));
            Assert.Equal(Loc.Current[reason], start.ToolTip);
            Assert.Empty(launcher.Starts);
            if (reason == "Vm.PickInstance")
            {
                var instance = (ComboBox)panel.FindName("Instance");
                Assert.Equal(-1, instance.SelectedIndex);
                instance.SelectedItem = 200;
                WpfContext.Drain();
                Assert.True(start.IsEnabled);
                Click(start);
                Assert.Equal(200, Assert.Single(launcher.Starts).Process);
                Click((Button)panel.FindName("Stop"));
                WpfContext.DrainUntil(() => !view.Running, "The stopped child did not reach the page.");
            }
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void ServiceFailure_ShowsPlainRedactedHostErrorWithAccessibleStatus(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => With(window =>
        {
            var fake = new FakeVm { StoredKey = FakeVm.Key };
            var launcher = new FakeVmLauncher();
            using VmView view = fake.Create(launcher: launcher);
            VmPanel panel = Attach(window, view);
            Click((Button)panel.FindName("Start"));
            launcher.Child.Exit.SetResult("Address already in use: " + FakeVm.Key);
            WpfContext.DrainUntil(() => !view.Running, "The failed child did not reach the page.");
            var status = (TextBlock)panel.FindName("ServiceStatus");
            Assert.Contains(Loc.Current["Vm.Failed"], status.Text, StringComparison.Ordinal);
            Assert.Contains("Address already in use", status.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeVm.Key, status.Text, StringComparison.Ordinal);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.True(((Button)panel.FindName("Start")).IsEnabled);
        }));

    private static VmPanel Attach(MainWindow window, VmView view)
    {
        window.Width = 960;
        window.AttachVm(view);
        window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
        WpfContext.Drain();
        var clients = (FrameworkElement)window.FindName("ClientsPage");
        return (VmPanel)clients.FindName("VmConnection");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
