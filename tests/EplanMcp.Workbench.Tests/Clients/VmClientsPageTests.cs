using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Clients;

[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class VmClientsPageTests(WpfContext wpf)
{
    private readonly FakeHost _host = new() { Answer = VmClientsViewTests.Answer };
    private static readonly string[] _inputs = ["VmAddress", "VmPort", "VmPassword"];
    private static readonly string[] _locked = ["LocalChoice", "VmChoice", "VmAddress", "VmPort", "VmPassword", "TestVm", "WriteButton", "RemoveButton"];
    private static readonly string[] _commands = ["TestVm", "WriteButton", "RemoveButton", "RefreshButton"];

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Modes_HaveNamesVmFieldsAndWarning_KeepTheVmPanel_AndClearPasswordOnLeaving(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, view) =>
        {
            Assert.Equal(11, ((ItemsControl)page.FindName("Cards")).Items.Count);
            Assert.Equal("8766", ((TextBox)page.FindName("VmPort")).Text);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)page.FindName("VmHost")).Visibility);
            Assert.Equal(Loc.Current["VmHost.Local"], Peer((RadioButton)page.FindName("LocalChoice")).GetName());
            Assert.Equal(Loc.Current["VmHost.Remote"], Peer((RadioButton)page.FindName("VmChoice")).GetName());
            EnterVm(page);
            Assert.Equal(10, ((ItemsControl)page.FindName("Cards")).Items.Count);
            Assert.DoesNotContain(((ItemsControl)page.FindName("Cards")).Items.Cast<ClientCard>(), c => c.Name == "claude-desktop");
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)page.FindName("LocalPlugin")).Visibility);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)page.FindName("VmConnection")).Visibility);
            Assert.Contains(Descendants<TextBlock>(page), t => t.Text == Loc.Current["VmHost.Plaintext"]);
            foreach (string name in _inputs)
            {
                var input = (FrameworkElement)page.FindName(name);
                Assert.NotNull(AutomationProperties.GetLabeledBy(input));
                Assert.NotEmpty(Peer(input).GetName());
                Assert.DoesNotContain(VmClientsViewTests.Key, Peer(input).GetName() + AutomationProperties.GetHelpText(input), StringComparison.Ordinal);
            }
            var password = (PasswordBox)page.FindName("VmPassword");
            Assert.True(Peer(password).IsPassword());
            Assert.Equal(WorkbenchDialog.Plain(Loc.Current["VmHost.Key"]), Peer(password).GetName());
            Fill(page);
            Assert.Equal(VmClientsViewTests.Key, password.Password);
            Assert.True(((Button)page.FindName("TestVm")).IsEnabled);
            ((RadioButton)page.FindName("LocalChoice")).IsChecked = true;
            WpfContext.Drain();
            Assert.Empty(password.Password);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)page.FindName("LocalPlugin")).Visibility);
            EnterVm(page);
            Assert.NotEmpty(view.VmKeyError);
            Assert.Empty(password.Password);
            Assert.False(((Button)page.FindName("TestVm")).IsEnabled);
            Assert.Single(_host.Runs); // The initial local detection only; changing modes never sends credentials.
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void Validation_HasInlineErrorsAndDisabledReasons_AndRemoveNeedsNoKey(AppLanguage language) =>
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
                Assert.Equal(AutomationProperties.GetHelpText(button), button.ToolTip);
            }
            ((TextBox)page.FindName("VmAddress")).Text = VmClientsViewTests.Address;
            ((TextBox)page.FindName("VmPort")).Text = "65536";
            WpfContext.Drain();
            Assert.Equal(Loc.Current["Vm.PortError"], AutomationProperties.GetHelpText(test));
            Assert.Equal(Loc.Current["Vm.PortError"], ((TextBlock)page.FindName("VmPortError")).Text);
            ((TextBox)page.FindName("VmPort")).Text = "8766";
            WpfContext.Drain();
            Assert.Equal(Loc.Current["VmHost.KeyError"], AutomationProperties.GetHelpText(test));
            Assert.Equal(Loc.Current["VmHost.KeyError"], AutomationProperties.GetHelpText((PasswordBox)page.FindName("VmPassword")));
            Assert.True(((Button)page.FindName("RemoveButton")).IsEnabled);
            Fill(page);
            Assert.True(test.IsEnabled);
            Assert.True(write.IsEnabled);
            Assert.Empty(AutomationProperties.GetHelpText(test));
            Assert.Single(_host.Runs);
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void TestPreviewWriteRemove_UseStdin_AndConfirmationShowsOnlyRedactedVmDetails(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, view) =>
        {
            EnterVm(page);
            Fill(page);
            Click((Button)page.FindName("TestVm"));
            Assert.Contains("eplan-vm", ((TextBlock)page.FindName("VmTestStatus")).Text, StringComparison.Ordinal);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((TextBlock)page.FindName("VmTestStatus")));
            view.Cards.Single(c => c.Name == "cursor").IsSelected = true;
            bool confirmed = false;
            int questions = 0;
            page.Confirm = (_, message, _, _, details) =>
            {
                questions++;
                Assert.Contains("eplan-vm", message, StringComparison.Ordinal);
                Assert.DoesNotContain(VmClientsViewTests.Key, message + details, StringComparison.Ordinal);
                if (questions < 3)
                { Assert.Contains("+ Bearer <key>", details, StringComparison.Ordinal); }
                return confirmed;
            };
            Click((Button)page.FindName("WriteButton"));
            Assert.Contains("--dry-run", _host.Runs[^1].Arguments);
            Assert.DoesNotContain(_host.Runs, r => r.Arguments.Contains("--vm") && !r.Arguments.Contains("--dry-run") && r.Arguments[0] != "vm-test");
            confirmed = true;
            Click((Button)page.FindName("WriteButton"));
            Assert.Equal(ClientResultKind.Written, view.Selected.Single().Result!.Result);
            ((PasswordBox)page.FindName("VmPassword")).Clear();
            WpfContext.Drain();
            Click((Button)page.FindName("RemoveButton"));
            Assert.Equal(ClientResultKind.Removed, view.Selected.Single().Result!.Result);
            Assert.Contains("--remove", _host.Runs[^1].Arguments);
            Assert.Null(_host.Inputs[^1]);
            Assert.All(_host.Inputs.Skip(1).SkipLast(1), value => Assert.Equal(VmClientsViewTests.Key, value));
            Assert.All(_host.Runs, run => Assert.DoesNotContain(VmClientsViewTests.Key, string.Join(' ', run.Arguments), StringComparison.Ordinal));
            Assert.All(Descendants<FrameworkElement>(page), element =>
            {
                Assert.DoesNotContain(VmClientsViewTests.Key, AutomationProperties.GetName(element) + AutomationProperties.GetHelpText(element) + element.ToolTip, StringComparison.Ordinal);
                if (element is TextBlock text)
                { Assert.DoesNotContain(VmClientsViewTests.Key, text.Text, StringComparison.Ordinal); }
                if (element is TextBox box)
                { Assert.DoesNotContain(VmClientsViewTests.Key, box.Text, StringComparison.Ordinal); }
            });
        }));

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void BusyTest_DisablesModeAndFieldsAndActions_WithReasons_ThenShowsFailure(AppLanguage language) =>
        wpf.RunWithLanguage(language, () => WithPage((page, _) =>
        {
            EnterVm(page);
            Fill(page);
            TaskCompletionSource<HostRun> held = _host.Hold();
            Click((Button)page.FindName("TestVm"));
            foreach (string name in _locked)
            {
                var control = (Control)page.FindName(name);
                Assert.False(control.IsEnabled);
                Assert.Equal(Loc.Current["Clients.Running"], AutomationProperties.GetHelpText(control));
                Assert.Equal(Loc.Current["Clients.Running"], control.ToolTip);
            }
            Assert.Equal(Loc.Current["Clients.Running"], AutomationProperties.GetHelpText((Button)page.FindName("TestVm")));
            Assert.Equal(Visibility.Visible, ((ProgressBar)page.FindName("Working")).Visibility);
            held.SetResult(new(1, Snapshots.Example("vm-test-json", "unauthorized")));
            WpfContext.DrainUntil(() => ((Button)page.FindName("TestVm")).IsEnabled, "Connection test did not finish.");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)page.FindName("ErrorBanner")).Visibility);
            Assert.Contains(Loc.Current["VmHost.Unauthorized"], ((TextBlock)page.FindName("ErrorText")).Text, StringComparison.Ordinal);
            Assert.True(((RadioButton)page.FindName("LocalChoice")).IsEnabled);
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
        ((PasswordBox)page.FindName("VmPassword")).Password = VmClientsViewTests.Key;
        WpfContext.Drain();
    }

    private static AutomationPeer Peer(FrameworkElement element) => UIElementAutomationPeer.CreatePeerForElement(element)!;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T item)
            { yield return item; }
            foreach (T descendant in Descendants<T>(child))
            { yield return descendant; }
        }
    }
}
