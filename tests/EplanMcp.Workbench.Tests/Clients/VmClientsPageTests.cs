using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Tests.Commands;
using EplanMcp.Workbench.Tests.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Clients;

[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class VmClientsPageTests(WpfContext wpf)
{
    private readonly FakeHost _host = new() { Answer = VmClientsViewTests.Answer };
    private static readonly string[] _inputs = ["VmAddress", "VmPort"];
    private static readonly string[] _locked = ["LocalChoice", "VmChoice", "VmAddress", "VmPort", "TestVm", "WriteButton", "RemoveButton"];
    private static readonly string[] _commands = ["TestVm", "WriteButton", "RemoveButton", "RefreshButton"];

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Modes_HaveNamesVmFieldsAndNote_WithoutSharedKeyField(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, _) =>
        {
            Assert.Equal(11, ((ItemsControl)page.FindName("Cards")).Items.Count);
            Assert.Equal("8766", ((TextBox)page.FindName("VmPort")).Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)page.FindName("VmHost")).Visibility);
            Assert.Equal(Loc.Current["VmHost.Local"], Peer((RadioButton)page.FindName("LocalChoice")).GetName());
            Assert.Equal(Loc.Current["VmHost.Remote"], Peer((RadioButton)page.FindName("VmChoice")).GetName());
            Assert.Null(page.FindName("VmPassword"));
            EnterVm(page);
            Assert.Equal(10, ((ItemsControl)page.FindName("Cards")).Items.Count);
            Assert.DoesNotContain(((ItemsControl)page.FindName("Cards")).Items.Cast<ClientCard>(), c => c.Name == "claude-desktop");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)page.FindName("LocalPlugin")).Visibility);
            Assert.Contains(Descendants<TextBlock>(page), t => t.Text == Loc.Current["VmHost.Plaintext"]);
            foreach (string name in _inputs)
            {
                var input = (FrameworkElement)page.FindName(name);
                Assert.NotNull(AutomationProperties.GetLabeledBy(input));
                Assert.NotEmpty(Peer(input).GetName());
            }

            Fill(page);
            Assert.True(((Button)page.FindName("TestVm")).IsEnabled);
            ((RadioButton)page.FindName("LocalChoice")).IsChecked = true;
            WpfContext.Drain();
            Assert.Equal(Visibility.Visible, ((FrameworkElement)page.FindName("LocalPlugin")).Visibility);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Validation_HasInlineErrorsAndDisabledReasons(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, _) =>
        {
            EnterVm(page);
            var test = (Button)page.FindName("TestVm");
            var write = (Button)page.FindName("WriteButton");
            var refresh = (Button)page.FindName("RefreshButton");
            foreach (Button button in new[] { test, write, refresh })
            {
                Assert.False(button.IsEnabled);
                Assert.Equal(Loc.Current["Vm.HostError"], AutomationProperties.GetHelpText(button));
            }

            ((TextBox)page.FindName("VmAddress")).Text = VmClientsViewTests.Address;
            ((TextBox)page.FindName("VmPort")).Text = "65536";
            WpfContext.Drain();
            Assert.Equal(Loc.Current["Vm.PortError"], AutomationProperties.GetHelpText(test));
            ((TextBox)page.FindName("VmPort")).Text = "8766";
            WpfContext.Drain();
            Assert.True(test.IsEnabled);
            Assert.True(write.IsEnabled);
            Assert.Empty(AutomationProperties.GetHelpText(test));
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void TestPreviewWriteRemove_WorkKeyFree(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, view) =>
        {
            EnterVm(page);
            Fill(page);
            Click((Button)page.FindName("TestVm"));
            Assert.Contains("eplan-vm", ((TextBlock)page.FindName("VmTestStatus")).Text, StringComparison.Ordinal);
            view.Cards.Single(c => c.Name == "cursor").IsSelected = true;
            bool confirmed = true;
            page.Confirm = (_, message, _, _, _) =>
            {
                Assert.Contains("eplan-vm", message, StringComparison.Ordinal);
                return confirmed;
            };
            Click((Button)page.FindName("WriteButton"));
            Assert.Equal(ClientResultKind.Written, view.Selected.Single().Result!.Result);
            Click((Button)page.FindName("RemoveButton"));
            Assert.Equal(ClientResultKind.Removed, view.Selected.Single().Result!.Result);
            Assert.All(_host.Inputs, Assert.Null);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void BusyTest_DisablesModeAndFieldsAndActions_ThenShowsFailure(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, _) =>
        {
            using var toasts = new ToastLog();
            EnterVm(page);
            Fill(page);
            TaskCompletionSource<HostRun> held = _host.Hold();
            Click((Button)page.FindName("TestVm"));
            foreach (string name in _locked)
            {
                var control = (Control)page.FindName(name);
                Assert.False(control.IsEnabled);
                Assert.Equal(Loc.Current["Clients.Running"], AutomationProperties.GetHelpText(control));
            }

            Assert.Equal(Visibility.Visible, ((ProgressBar)page.FindName("Working")).Visibility);
            held.SetResult(new(1, Snapshots.Example("vm-test-json", "forbidden")));
            WpfContext.DrainUntil(() => ((Button)page.FindName("TestVm")).IsEnabled, "Connection test did not finish.");
            Assert.Contains(Loc.Current["VmHost.Forbidden"], ((TextBlock)page.FindName("ErrorText")).Text, StringComparison.Ordinal);
            Assert.Contains(toasts.Heard, h => h.Tone == ToastTone.Error && h.Text.Contains(Loc.Current["VmHost.Forbidden"], StringComparison.Ordinal));
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void MissingHost_DisablesVmCommandsWithLocalizedReasons(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, _) =>
        {
            EnterVm(page);
            Fill(page);
            foreach (string name in _commands)
            {
                var control = (Button)page.FindName(name);
                Assert.False(control.IsEnabled);
                Assert.Equal(Loc.Current["Host.Missing"], AutomationProperties.GetHelpText(control));
            }

            Assert.Empty(_host.Runs);
        }, available: false));

    private void WithPage(Action<ClientsPage, ClientsView> test, bool available = true) => With(window =>
    {
        var page = (ClientsPage)window.FindName("ClientsPage");
        var view = new ClientsView(new(available ? _host : null), "C:\\unused", _ => false);
        window.AttachClients(view);
        window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
        WpfContext.Drain();
        test(page, view);
    });

    private static void EnterVm(ClientsPage page)
    {
        ((RadioButton)page.FindName("VmChoice")).IsChecked = true;
        WpfContext.Drain();
    }

    private static void Fill(ClientsPage page)
    {
        ((TextBox)page.FindName("VmAddress")).Text = VmClientsViewTests.Address;
        ((TextBox)page.FindName("VmPort")).Text = "8766";
        WpfContext.Drain();
    }

    private static AutomationPeer Peer(FrameworkElement element) => UIElementAutomationPeer.CreatePeerForElement(element)!;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T item)
            {
                yield return item;
            }

            foreach (T descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
