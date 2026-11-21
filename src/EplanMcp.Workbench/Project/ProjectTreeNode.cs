using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Project;

/// <summary>
/// One row of the project tree (Workbench 4.0 design): a group, a page-type folder, a page, or a group's device list.
/// A page carries its full name; a group and its device list carry the structure prefix.
/// </summary>
internal sealed class ProjectTreeNode(string title, string sub, string badge, string count, bool isGroup)
{
    public string Title { get; } = title;

    public string Sub { get; } = sub;

    public string Badge { get; } = badge;

    public string Count { get; } = count;

    public bool IsGroup { get; } = isGroup;

    public List<ProjectTreeNode> Children { get; } = [];

    /// <summary>The page shown when this row is chosen; null for anything else.</summary>
    public ProjectPageNode? Page { get; init; }

    /// <summary>The structure prefix whose devices this row lists; null for anything else.</summary>
    public string? DevicesPrefix { get; init; }

    /// <summary>
    /// The rows of <paramref name="groups"/>, keeping only the pages whose name or description holds
    /// <paramref name="filter"/>, and the groups and folders that still have one.
    /// </summary>
    public static IReadOnlyList<ProjectTreeNode> From(IReadOnlyList<ProjectGroup> groups, string projectName, string filter)
    {
        Loc loc = Loc.Current;
        var rows = new List<ProjectTreeNode>();
        foreach (ProjectGroup group in groups)
        {
            string title = group.Prefix.Length > 0 ? group.Prefix : projectName;
            var groupNode = new ProjectTreeNode(title, string.Empty, string.Empty, string.Empty, true);
            foreach (ProjectFolder folder in group.Folders)
            {
                ProjectPageNode[] pages = [.. folder.Pages.Where(p => filter.Length == 0
                    || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))];
                if (pages.Length == 0)
                {
                    continue;
                }

                var folderNode = new ProjectTreeNode(folder.Type, string.Empty, string.Empty, pages.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), false);
                folderNode.Children.AddRange(pages.Select(p => new ProjectTreeNode(p.Name, p.Description, p.Kind, string.Empty, false) { Page = p }));
                groupNode.Children.Add(folderNode);
            }

            if (groupNode.Children.Count == 0)
            {
                continue;
            }

            groupNode.Children.Add(new ProjectTreeNode(loc["Project.DeviceList"], string.Empty, string.Empty, string.Empty, false) { DevicesPrefix = group.Prefix });
            rows.Add(groupNode);
        }

        return rows;
    }
}
