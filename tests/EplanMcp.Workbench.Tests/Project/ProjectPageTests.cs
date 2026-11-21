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
            Until(() => devices.ItemsSource is IReadOnlyList<ProjectDeviceRow> { Count: 1 });

            Assert.Equal("=GAA+A1-Q1", ((IReadOnlyList<ProjectDeviceRow>)devices.ItemsSource)[0].VisibleTag);
            Assert.Equal("=GAA+A1&EFS1/1", _tools.Calls[^1].Args["page"]?.GetValue<string>());
            Assert.Equal("=GAA+A1&EFS1/1", Text(page, "HeaderTitle"));

            ProjectTreeNode deviceList = groups[1].Children[^1];
            tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object>(first, deviceList, TreeView.SelectedItemChangedEvent));
            Until(() => _tools.Calls[^1].Args["tag_contains"] is not null);
            Assert.Equal("=GAA+A2", _tools.Calls[^1].Args["tag_contains"]!.GetValue<string>());
        }));

        Assert.All(_tools.Calls, c => Assert.Contains(c.Tool, HostToolClient.ReadTools));
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
        _ => throw new InvalidOperationException("not expected: " + tool),
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
