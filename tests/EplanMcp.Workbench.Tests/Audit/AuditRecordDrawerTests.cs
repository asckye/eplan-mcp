using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Audit;

/// <summary>
/// An audit record's details (M5 design 4.4 item 2, workbench-ui 3.1, DLG-10, CTL-04, CTL-12, CTL-13): Enter on a row
/// opens every field of the record in a modal pane, focus on its title; Esc gives focus back to the row.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class AuditRecordDrawerTests(WpfContext wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eplanmcp-workbench-record-").FullName;
    private readonly List<string> _opened = [];

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void EnterOnARow_OpensEveryFieldOfTheRecord_AndEscGivesTheFocusBack()
    {
        string tool = "eplan_page_create" + (char)0x202E;
        File.WriteAllText(
            Path.Combine(_directory, "2026-10-20.jsonl"),
            $$"""{"time":"2026-10-20T12:00:00Z","kind":"approval","tool":{{JsonSerializer.Serialize(tool)}},"changes":["create page"],"decision":"granted"}""" + "\n",
            new UTF8Encoding(false));
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            window.AttachAudit(new AuditView(_directory, new HostCommands(new FakeHost()), new ManualTime(DateTimeOffset.UtcNow)), new FakeShell(_opened));
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            var page = (AuditPage)window.FindName("AuditPage");
            var list = (ListView)page.FindName("Records");
            Until(() => list.Items.Count == 1);
            list.SelectedIndex = 0;
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            row.Focus();

            row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(row), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            WpfContext.Drain();

            var drawer = (AuditRecordDrawer)window.FindName("RecordPane");
            Assert.Same(drawer.FindName("Heading"), Keyboard.FocusedElement);
            Assert.Equal("2026-10-20.jsonl，第 1 行", ((TextBlock)drawer.FindName("Place")).Text);
            var fields = ((ItemsControl)drawer.FindName("Fields")).Items.Cast<KeyValuePair<string, string>>().ToList();
            Assert.Equal(["time", "kind", "tool", "changes", "decision"], fields.Select(f => f.Key));
            Assert.Equal(("eplan_page_create", "[\"create page\"]"), (fields[2].Value, fields[3].Value));
            Assert.False(MainWindow.NavigateCommand.CanExecute(WorkbenchPage.Status, window)); // nothing behind it takes input
            Assert.True(AutomationProperties.GetIsDialog((DependencyObject)drawer.Content));

            Click((Button)drawer.FindName("FolderButton"));
            Assert.Equal([_directory], _opened);
            drawer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(drawer), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            WpfContext.Drain();

            Assert.False(((FrameworkElement)window.FindName("RecordLayer")).IsVisible);
            Assert.Same(row, Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void AFolderWindowsCannotOpen_IsSaidInTheDrawer()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            window.AttachAudit(new AuditView(_directory, new HostCommands(new FakeHost()), new ManualTime(DateTimeOffset.UtcNow)), new FakeShell(_opened, works: false));
            window.OpenRecord(Record());
            var drawer = (AuditRecordDrawer)window.FindName("RecordPane");

            Click((Button)drawer.FindName("FolderButton"));

            var problem = (TextBlock)drawer.FindName("FolderProblem");
            Assert.True(problem.IsVisible);
            Assert.Equal($"Windows 无法打开 {_directory}。", problem.Text);
            window.CloseRecord();
        }));
    }

    [Fact]
    public void AReminder_ClosesTheDrawers_BeforeItOpensTheQueue()
    {
        wpf.Run(() => With(window =>
        {
            window.AttachAudit(new AuditView(_directory, new HostCommands(new FakeHost()), new ManualTime(DateTimeOffset.UtcNow)), new FakeShell(_opened));
            window.OpenRecord(Record());

            window.Open(null);
            Assert.False(((FrameworkElement)window.FindName("RecordLayer")).IsVisible);
            window.OpenSettings();
            window.Open(null);

            Assert.False(((FrameworkElement)window.FindName("SettingsLayer")).IsVisible);
            Assert.True(MainWindow.NavigateCommand.CanExecute(WorkbenchPage.Status, window));
        }));
    }

    private static AuditRecord Record() => new("2026-10-20.jsonl", 1, null, [KeyValuePair.Create("kind", "approval")]);

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

    private sealed class FakeShell(List<string> opened, bool works = true) : IShellOpen
    {
        public bool Open(string path)
        {
            opened.Add(path);
            return works;
        }
    }
}
