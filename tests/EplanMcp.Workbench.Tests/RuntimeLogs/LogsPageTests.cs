using System.Windows;
using System.Windows.Controls;
using EplanMcp.Workbench.Logs;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Logs;

/// <summary>
/// The runtime logs page (Workbench 4.0 design): one row per entry with its level, and "Clear…", which asks first and
/// hides only what the view shows; the files stay and newer entries show again.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class LogsPageTests(WpfContext wpf)
{
    [Fact]
    public void Clear_AsksFirst_HidesTheShownEntries_KeepsTheFiles_AndNewEntriesShowAgain()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("eplanmcp-logs-");
        try
        {
            string folder = Path.Combine(root.FullName, "logs");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "workbench-20261121.log");
            File.WriteAllLines(file, ["2026-11-21 10:00:01 INFO started", "2026-11-21 10:00:02 WARN slow pipe"]);

            wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
            {
                var view = new LogsView(folder, Path.Combine(root.FullName, "config.json"));
                window.AttachLogs(view);
                window.Navigate(WorkbenchPage.Logs, NavigationOrigin.Menu);
                view.Reload();
                WpfContext.Drain();
                var page = (LogsPage)window.FindName("LogsPage");
                var list = (ListBox)page.FindName("LogList");
                var clear = (Button)page.FindName("ClearButton");
                Assert.Equal(2, list.Items.Count);
                Assert.Equal("! WARN", Assert.IsType<LogRow>(list.Items.Cast<LogRow>().First(r => r.Level == "WARN")).LevelText);

                page.ConfirmClear = _ => false;
                Click(clear);
                Assert.Equal(2, list.Items.Count); // declined: nothing changes

                int asked = 0;
                page.ConfirmClear = n => (asked = n) > 0;
                Click(clear);
                Assert.Equal(2, asked);
                Assert.Empty(list.Items);
                Assert.True(((FrameworkElement)page.FindName("Empty")).IsVisible);
                Assert.False(clear.IsEnabled);
                Assert.Equal(2, File.ReadAllLines(file).Length); // the file stays

                File.AppendAllLines(file, ["2026-11-21 10:00:03 ERROR pipe closed"]);
                view.Reload();
                WpfContext.Drain();
                LogRow row = Assert.Single(list.Items.Cast<LogRow>());
                Assert.Equal("✕ ERROR", row.LevelText);
            }));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("workbench-20261121.log", "2026-11-21T14:18:13.154Z Information Approval pipe is listening", "14:18:13", "INFO", "Workbench", "Approval pipe is listening")]
    [InlineData("bridge-20261121.log", "2026-11-21 03:55:14.225 [Warning] Slow read", "03:55:14", "WARN", "Bridge", "Slow read")]
    [InlineData("bridge-20261121.log", "2026-11-21 03:55:14.225 [Error] Pipe closed", "03:55:14", "ERROR", "Bridge", "Pipe closed")]
    [InlineData("workbench-20261121.log", "2026-11-21T14:18:13.154Z Information Retry after FAIL of the probe", "14:18:13", "INFO", "Workbench", "Retry after FAIL of the probe")]
    [InlineData("bridge-startup-error.log", "something without a time", "--:--:--", "INFO", "Bridge", "something without a time")]
    public void ALine_IsReadForItsTimeLevelProgramAndMessage_AndOnlyTheLevelWordCounts(string file, string line, string time, string level, string source, string message) =>
        Assert.Equal(new LogRow(time, level, source, message), LogsView.ParseLine(file, line));
}
