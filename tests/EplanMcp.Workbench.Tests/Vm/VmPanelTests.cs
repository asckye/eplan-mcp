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
    private static readonly string[] _inputs = ["Address", "Port", "HostAddress", "KeyValue", "FirewallValue"];
    private static readonly string[] _commands = ["Generate", "Reveal", "Copy", "Delete", "Allow", "Remove", "Refresh"];
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
                Assert.False(string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(input)!.GetName()));
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
            Assert.Single(fake.Host.Runs);
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
            Assert.Single(fake.Host.Runs);
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
