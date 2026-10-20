using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of connection.generate.plan and connection.generate.execute
/// (docs/tools/eplan_connection_generate.md, sections 2 and 5): the project and each page, found as eplan_page_get
/// finds a page: exactly, or ignoring case when only one page matches.
/// </summary>
internal sealed class ConnectionGenerateRequest
{
    public const int MaxPages = 50;

    private ConnectionGenerateRequest(OpenProject project, string[] pages)
    {
        Project = project;
        Pages = pages;
    }

    public OpenProject Project { get; }

    /// <summary>The pages' full names as EPLAN has them, in the order given.</summary>
    public IReadOnlyList<string> Pages { get; }

    /// <summary>Every argument problem at once (mcp-tool-design 6.2) as INVALID_ARGUMENT, then the project and every missing page.</summary>
    public static ConnectionGenerateRequest Check(ConnectionGenerateArgs args, IProjectCatalog catalog, IPageReader reader)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<string> pages = args.Pages ?? [];
        if (pages.Count is < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {MaxPages.ToString(CultureInfo.InvariantCulture)} full page names.");
        }

        for (int i = 0; i < pages.Count && i < MaxPages; i++)
        {
            string at = "pages[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            int same = pages.FindIndex(0, i, p => string.Equals(p, pages[i], StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(pages[i]))
            {
                violations.Add($"{at} is required: a full page name as eplan_page_list returns it.");
            }
            else if (same >= 0)
            {
                violations.Add($"{at} repeats pages[{same.ToString(CultureInfo.InvariantCulture)}] (letter case does not count).");
            }
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<string> names = reader.PageNames(project.Path);
        string?[] found = [.. pages.Select(page => PageUpdateRequest.Find(names, page))];
        string[] missing = [.. pages.Where((_, i) => found[i] is null)];
        return missing.Length > 0
            ? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has these names, so nothing was changed. Use eplan_page_list with name_prefix to find the exact page names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["missing"] = string.Join("\n", missing) })
            : new ConnectionGenerateRequest(project, [.. found.OfType<string>()]);
    }

    /// <summary>
    /// The plan's state (tool spec 5): each page's connections by their ends, and the session identifier of every object
    /// on it. A connection generated or deleted there, or an object placed, removed or replaced, changes it.
    /// </summary>
    public string StateFingerprint(IReadOnlyList<IReadOnlyList<ConnectionRecord>> connections, IReadOnlyList<IReadOnlyList<string>> objects) =>
        Fingerprint.Of(Pages.SelectMany((page, i) => connections[i]
            .Select(c => page + "\tconnection\t" + ConnectionChanges.Key(c))
            .Concat(objects[i].Select(id => page + "\tobject\t" + id))));
}
