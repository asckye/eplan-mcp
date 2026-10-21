using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EplanMcp.Workbench.Settings;
using EplanMcp.Workbench.Shell;
using Microsoft.Win32;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Settings;

/// <summary>
/// Start at login and config.json in the settings drawer (M5 design 4.4 item 5, Q3, CMD-08, ERR-03, NAV-10): the Run
/// value of the current user only, written and removed at once, the switch showing what the registry holds; config.json
/// read only. Each test has a Run key of its own under HKCU, removed afterwards.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class StartAtLoginTests(WpfContext wpf) : IDisposable
{
    private const string Executable = @"C:\Users\engineer\EplanMcp\app\workbench\EplanMcp.Workbench.exe";

    private const string Tests = @"Software\EplanMcp.Tests";

    private readonly string _test = Tests + @"\" + Guid.NewGuid().ToString("N");
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-workbench-settings-");
    private readonly List<string> _opened = [];
    private bool _shellWorks = true;

    private string RunKey => _test + @"\Run";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_test, throwOnMissingSubKey: false);
        bool empty;
        using (RegistryKey? tests = Registry.CurrentUser.OpenSubKey(Tests))
        {
            empty = tests is { SubKeyCount: 0, ValueCount: 0 };
        }

        if (empty)
        {
            Registry.CurrentUser.DeleteSubKey(Tests, throwOnMissingSubKey: false);
        }

        _root.Delete(recursive: true);
    }

    [Fact]
    public void TurningItOnAndOff_WritesAndRemovesOnlyItsOwnValue_Quoted()
    {
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue("Other", "other.exe");
        }

        var login = new StartAtLogin(RunKey, Executable);
        Assert.False(login.IsOn);

        Assert.True(login.Set(true));
        Assert.True(login.IsOn);
        Assert.Equal($"\"{Executable}\"", Value(StartAtLogin.ValueName));
        Assert.True(login.Set(false));
        Assert.True(login.Set(false));

        Assert.False(login.IsOn);
        Assert.Null(Value(StartAtLogin.ValueName));
        Assert.Equal("other.exe", Value("Other"));
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", StartAtLogin.RunKey);
    }

    [Fact]
    public void TheWorkbench_NeverWritesTheLocalMachine()
    {
        Assert.DoesNotContain(SourceScan.Own, f => f.Text.Contains("LocalMachine", StringComparison.Ordinal) || f.Text.Contains("HKEY_LOCAL_MACHINE", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDrawer_ShowsWhatTheRegistryHolds_AndItsSwitchAppliesAtOnce()
    {
        new StartAtLogin(RunKey, Executable).Set(true);
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithDrawer((window, drawer) =>
        {
            var toggle = (ToggleButton)drawer.FindName("LoginSwitch");
            Assert.True(toggle.IsChecked);
            Assert.Equal("登录 Windows 时启动 Workbench", AutomationProperties.GetName(toggle));

            Toggle(toggle);
            Assert.False(toggle.IsChecked);
            Assert.Null(Value(StartAtLogin.ValueName));
            Toggle(toggle);
            Assert.Equal($"\"{Executable}\"", Value(StartAtLogin.ValueName));
        }));
    }

    [Fact]
    public void WhenWindowsRefuses_TheSwitchGoesBack_AndSaysSoInPlace()
    {
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            RegistrySecurity security = run.GetAccessControl();
            security.AddAccessRule(new RegistryAccessRule(WindowsIdentity.GetCurrent().User!, RegistryRights.SetValue, AccessControlType.Deny));
            run.SetAccessControl(security);
        }

        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithDrawer((window, drawer) =>
        {
            var toggle = (ToggleButton)drawer.FindName("LoginSwitch");

            Toggle(toggle);

            Assert.False(toggle.IsChecked);
            Assert.True(((FrameworkElement)drawer.FindName("LoginError")).IsVisible);
            Assert.Null(Value(StartAtLogin.ValueName));
        }));
    }

    [Fact]
    public void ConfigJson_IsShownReadOnly_AsPlainText_AndOpensInItsOwnProgram()
    {
        string config = Path.Combine(_root.FullName, "config.json");
        File.WriteAllText(config, "{\n  \"writesOutsideScratch\": \"approve\"\u202E\n}");
        wpf.Run(() => WithDrawer((window, drawer) =>
        {
            var text = (TextBox)drawer.FindName("ConfigText");
            Assert.True(text.IsReadOnly);
            Assert.Equal(string.Join(Environment.NewLine, "{", "  \"writesOutsideScratch\": \"approve\"", "}"), text.Text);

            Click((Button)drawer.FindName("ConfigButton"));
            Assert.Equal([config], _opened);
        }));
    }

    [Fact]
    public void AConfigWindowsCannotOpen_IsSaidBesideTheButton()
    {
        _shellWorks = false;
        string config = Path.Combine(_root.FullName, "config.json");
        File.WriteAllText(config, "{}");
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithDrawer((window, drawer) =>
        {
            Click((Button)drawer.FindName("ConfigButton"));

            var problem = (TextBlock)drawer.FindName("ConfigProblem");
            Assert.True(problem.IsVisible);
            Assert.Equal($"Windows 无法打开 {config}。", problem.Text);
        }));
    }

    [Fact]
    public void WithoutConfigJson_TheDefaultsApply_AndThereIsNothingToOpen()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithDrawer((window, drawer) =>
        {
            Assert.Equal("没有 config.json：使用默认值。", ((TextBox)drawer.FindName("ConfigText")).Text);
            Assert.False(((Button)drawer.FindName("ConfigButton")).IsEnabled);
        }));
    }

    private void WithDrawer(Action<MainWindow, SettingsDrawer> test) => With(window =>
    {
        window.AttachSettings(new StartAtLogin(RunKey, Executable), Path.Combine(_root.FullName, "config.json"), new FakeShell(_opened, _shellWorks));
        window.OpenSettings();
        test(window, (SettingsDrawer)window.FindName("SettingsPane"));
        window.CloseSettings();
    });

    /// <summary>Turns the switch as a screen reader or the keyboard does, through its toggle pattern.</summary>
    private static void Toggle(ToggleButton toggle)
    {
        ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
        WpfContext.Drain();
    }

    private string? Value(string name)
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
        return run?.GetValue(name) as string;
    }

    private sealed class FakeShell(List<string> opened, bool works = true) : IShellOpen
    {
        public bool Open(string path)
        {
            opened.Add(path);
            return works;
        }
    }
}
