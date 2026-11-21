using System.Text.Json.Nodes;
using EplanMcp.Workbench.Approvals;

namespace EplanMcp.Workbench.Project;

/// <summary>One page in the tree: its full name, the kind code shown as a badge (<c>EFS</c>), its type and description.</summary>
internal sealed record ProjectPageNode(string Name, string Kind, string Type, string Description);

/// <summary>A folder of a group: the pages of one page type.</summary>
internal sealed record ProjectFolder(string Type, IReadOnlyList<ProjectPageNode> Pages);

/// <summary>A group of the tree: one structure prefix (<c>=GAA+A1</c>) and its folders.</summary>
internal sealed record ProjectGroup(string Prefix, IReadOnlyList<ProjectFolder> Folders)
{
    public int PageCount => Folders.Sum(f => f.Pages.Count);
}

/// <summary>
/// The project tree of the Workbench 4.0 design, built from <c>eplan_page_list</c>: a page named <c>=GAA+A1&amp;EFS1/4</c>
/// belongs to the group <c>=GAA+A1</c>, carries the kind <c>EFS</c>, and sits in the folder of its page type. Every
/// text comes from the project and is made plain first (APR-08).
/// </summary>
internal static class ProjectTree
{
    /// <summary>The groups in the order their first page came, the folders likewise.</summary>
    public static IReadOnlyList<ProjectGroup> Build(IEnumerable<ProjectPageNode> pages) =>
    [
        .. pages.GroupBy(p => Prefix(p.Name), StringComparer.Ordinal)
            .Select(g => new ProjectGroup(g.Key, [.. g.GroupBy(p => p.Type, StringComparer.Ordinal).Select(f => new ProjectFolder(f.Key, [.. f]))])),
    ];

    /// <summary>The pages of one <c>eplan_page_list</c> result.</summary>
    public static IEnumerable<ProjectPageNode> Pages(JsonObject result) =>
        (result["pages"] as JsonArray ?? []).OfType<JsonObject>().Select(p =>
        {
            string name = ProjectText.Line(Text(p, "name"));
            return new ProjectPageNode(name, Kind(name), ProjectText.Line(Text(p, "type")), ProjectText.Line(Text(p, "description")));
        });

    /// <summary>The structure part of a page name, before <c>&amp;</c>; empty when the name has none.</summary>
    public static string Prefix(string name)
    {
        int amp = name.IndexOf('&', StringComparison.Ordinal);
        return amp > 0 ? name[..amp] : string.Empty;
    }

    /// <summary>The letters after <c>&amp;</c> up to the first digit or slash (<c>EFS</c> in <c>=GAA+A1&amp;EFS1/4</c>); else the first letters of the type.</summary>
    public static string Kind(string name)
    {
        int amp = name.IndexOf('&', StringComparison.Ordinal);
        string rest = amp >= 0 ? name[(amp + 1)..] : string.Empty;
        string letters = new([.. rest.TakeWhile(char.IsAsciiLetter)]);
        return letters.Length is > 0 and <= 4 ? letters.ToUpperInvariant() : string.Empty;
    }

    private static string Text(JsonObject node, string name) => node[name] is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;
}
