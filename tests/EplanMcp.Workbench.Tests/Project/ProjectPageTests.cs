using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using EplanMcp.Workbench.Project;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Project;

/// <summary>
/// The project page in a real window (ADR-0065 decision 2): it reads through the Host's read-only tools when shown, says
/// plainly when EPLAN is not there, builds the tree from the pages, and a chosen page or group reads its devices.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ProjectPageTests(WpfContext wpf)
{
    private readonly FakeTools _tools = new();

    [Fact]
    public void WithoutEplan_ThePageSaysSo_AndShowsTheHostsHint()
    {
        _tools.Answer = (tool, _) => tool == "eplan_session_status"
            ? new JsonObject { ["connected"] = false, ["hint"] = "Start EPLAN with the add-in." }
            : throw new InvalidOperationException("not expected");
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage(page =>
        {
            Until(() => _tools.Calls.Count > 0 && ((FrameworkElement)page.FindName("Empty")).IsVisible && Text(page, "CountsText").Length > 0);

            Assert.Equal(["eplan_session_status"], _tools.Calls.Select(c => c.Tool));
            var empty = (EmptyState)page.FindName("EmptyState");
            Assert.Equal(Loc.Current["Project.NotConnected"], empty.Title);
            Assert.Equal("Start EPLAN with the add-in.", empty.Text);
            Assert.Empty((IEnumerable<object>)((TreeView)page.FindName("Tree")).ItemsSource);
        }));
    }

    [Fact]
    public void Connected_TheTreeHoldsTheGroupsAndPages_AndAPageReadsItsDevices()
    {
        _tools.Answer = Connected;
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage(page =>
        {
            var tree = (TreeView)page.FindName("Tree");
            Until(() => tree.ItemsSource is IReadOnlyList<ProjectTreeNode> { Count: 2 });

            IReadOnlyList<ProjectTreeNode> groups = (IReadOnlyList<ProjectTreeNode>)tree.ItemsSource;
            Assert.Equal(["=GAA+A1", "=GAA+A2"], groups.Select(g => g.Title));
            ProjectTreeNode first = groups[0].Children[0].Children[0];
            Assert.Equal(("=GAA+A1&EFS1/1", "EFS", "Power supply"), (first.Title, first.Badge, first.Sub));
            Assert.Equal(Loc.Current.T("Project.Counts", 3, 2), Text(page, "CountsText"));
            Assert.Equal("EPLAN 2026.0.3 · Electric P8", Text(page, "HeaderEplan"));

            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(null!, first, TreeView.SelectedItemChangedEvent));
            var devices = (ItemsControl)page.FindName("Devices");
            Until(() => devices.ItemsSource is IReadOnlyList<ProjectDeviceRow> { Count: 1 } && _tools.Calls.Any(c => c.Tool == "eplan_check_list"));

            Assert.Equal("=GAA+A1-Q1", ((IReadOnlyList<ProjectDeviceRow>)devices.ItemsSource)[0].VisibleTag);
            Assert.Equal("=GAA+A1&EFS1/1", _tools.Calls.Last(c => c.Tool == "eplan_device_list").Args["page"]?.GetValue<string>());
            Assert.Equal("=GAA+A1&EFS1/1", Text(page, "HeaderTitle"));

            ProjectTreeNode deviceList = groups[1].Children[^1];
            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(first, deviceList, TreeView.SelectedItemChangedEvent));
            Until(() => _tools.Calls[^1].Args["tag_contains"] is not null);
            Assert.Equal("=GAA+A2", _tools.Calls[^1].Args["tag_contains"]!.GetValue<string>());
        }));

        Assert.All(_tools.Calls, c => Assert.Contains(c.Tool, HostToolClient.ReadTools));
    }

    [Fact]
    public void APage_ShowsItsNetList_AndOnlyItsOwnCheckMessages_ErrorsFirst()
    {
        _tools.Answer = Connected;
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage(page =>
        {
            var tree = (TreeView)page.FindName("Tree");
            Until(() => tree.ItemsSource is IReadOnlyList<ProjectTreeNode> { Count: 2 });
            ProjectTreeNode first = ((IReadOnlyList<ProjectTreeNode>)tree.ItemsSource)[0].Children[0].Children[0];
            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(null!, first, TreeView.SelectedItemChangedEvent));
            var checks = (ItemsControl)page.FindName("Checks");
            Until(() => checks.ItemsSource is IReadOnlyList<ProjectCheckRow> { Count: 2 });

            Assert.True(((FrameworkElement)page.FindName("PageTabs")).IsVisible);
            Assert.Equal("2", Text(page, "ReviewCount"));
            Choose(page, "TabSchematic");
            Assert.True(((FrameworkElement)page.FindName("NetlistPanel")).IsVisible);
            Assert.False(((FrameworkElement)page.FindName("DevicesPanel")).IsVisible);
            string[] lines = ((TextBox)page.FindName("NetlistText")).Text.Split(Environment.NewLine);
            Assert.Equal("DEVICE  -Q1              Circuit breaker", lines[0]);
            Assert.Equal("WIRE    -Q1:2            → -K1:1            1,5 GY", lines[1]);

            Choose(page, "TabReview");
            Assert.True(((FrameworkElement)page.FindName("ReviewPanel")).IsVisible);
            IReadOnlyList<ProjectCheckRow> rows = (IReadOnlyList<ProjectCheckRow>)checks.ItemsSource;
            Assert.Equal(["error", "warning"], rows.Select(r => r.Severity));
            Assert.Equal(("✕", "#4025 · Connections", "Open connection"), (rows[0].Glyph, rows[0].Code, rows[0].Text));
            Assert.Equal(Loc.Current.T("Project.ReviewSummary", 1, 1), Text(page, "ReviewSummary"));
        }));
    }

    [Fact]
    public void AToolError_ShowsReadFailed_WithTheHostsText_AndRefreshReadsAgain()
    {
        _tools.Answer = (tool, _) => tool == "eplan_session_status"
            ? new JsonObject { ["connected"] = true }
            : throw new ProjectToolException(tool, "No project is open.");
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage(page =>
        {
            var empty = (EmptyState)page.FindName("EmptyState");
            Until(() => empty.Title == Loc.Current["Project.ReadFailed"]);
            Assert.Equal("No project is open.", empty.Text);

            int before = _tools.Calls.Count;
            _tools.Answer = Connected;
            Click((UIElement)page.FindName("RefreshButton"));
            Until(() => ((TreeView)page.FindName("Tree")).ItemsSource is IReadOnlyList<ProjectTreeNode> { Count: 2 });
            Assert.True(_tools.Calls.Count > before);
        }));
    }

    private static JsonObject Connected(string tool, JsonObject args) => tool switch
    {
        "eplan_session_status" => new JsonObject { ["connected"] = true, ["eplan"] = new JsonObject { ["version"] = "2026.0.3", ["variant"] = "Electric P8" } },
        "eplan_project_get" => new JsonObject { ["project"] = new JsonObject { ["name"] = "Sample" }, ["counts"] = new JsonObject { ["pages"] = 3, ["devices"] = 2 } },
        "eplan_page_list" => new JsonObject
        {
            ["pages"] = new JsonArray(
                Page("=GAA+A1&EFS1/1", "Multi-line", "Power supply"),
                Page("=GAA+A1&EFS1/2", "Multi-line", "Motors"),
                Page("=GAA+A2&EFS2/1", "Multi-line", "Cabinet light")),
        },
        "eplan_device_list" => new JsonObject
        {
            ["devices"] = new JsonArray(new JsonObject
            {
                ["tag"] = "=GAA+A1-Q1",
                ["visibleTag"] = "=GAA+A1-Q1",
                ["category"] = "Circuit breaker",
                ["page"] = "=GAA+A1&EFS1/1",
                ["parts"] = 1,
            }),
        },
        "eplan_connection_list" => new JsonObject
        {
            ["connections"] = new JsonArray(new JsonObject
            {
                ["from"] = new JsonObject { ["device"] = "=GAA+A1-Q1", ["pin"] = "2" },
                ["to"] = new JsonObject { ["device"] = "=GAA+A1-K1", ["pin"] = "1" },
                ["page"] = "=GAA+A1&EFS1/1",
                ["crossSection"] = "1,5",
                ["colour"] = "GY",
                ["kind"] = "2d",
            }),
        },
        "eplan_check_list" => new JsonObject
        {
            ["messages"] = new JsonArray(
                Message(2003, "warning", "Devices", "Part missing", "Function", "=GAA+A1-Q1"),
                Message(9001, "error", "Pages", "Elsewhere", "Page", "=GAA+A2&EFS2/1"),
                Message(4025, "error", "Connections", "Open connection", "Page", "=GAA+A1&EFS1/1")),
        },
        _ => throw new InvalidOperationException("not expected: " + tool),
    };

    private static JsonObject Message(int number, string severity, string region, string text, string kind, string name) => new()
    {
        ["number"] = number,
        ["severity"] = severity,
        ["region"] = region,
        ["text"] = text,
        ["done"] = false,
        ["object"] = new JsonObject { ["kind"] = kind, ["name"] = name },
    };

    private static JsonObject Page(string name, string type, string description) =>
        new() { ["name"] = name, ["type"] = type, ["description"] = description };

    private void WithPage(Action<ProjectPage> test) => With(window =>
    {
        window.AttachProject(new ProjectView(_tools));
        window.Navigate(WorkbenchPage.Project, NavigationOrigin.Shortcut);
        WpfContext.Drain();
        test((ProjectPage)window.FindName("ProjectPage"));
    });

    private static void Choose(FrameworkElement page, string tab)
    {
        ((RadioButton)page.FindName(tab)).IsChecked = true;
        WpfContext.Drain();
    }

    private static string Text(FrameworkElement page, string name) => ((TextBlock)page.FindName(name)).Text;

    private static void Until(Func<bool> condition) => WpfContext.DrainUntil(condition, "Not in time.");

    private sealed class FakeTools : IProjectTools
    {
        public Func<string, JsonObject, JsonObject> Answer { get; set; } = (_, _) => [];

        public List<(string Tool, JsonObject Args)> Calls { get; } = [];

        public Task<JsonObject> ReadAsync(string tool, JsonObject args, CancellationToken cancellationToken)
        {
            Calls.Add((tool, (JsonObject)args.DeepClone()));
            return Task.FromResult(Answer(tool, args));
        }
    }
}
