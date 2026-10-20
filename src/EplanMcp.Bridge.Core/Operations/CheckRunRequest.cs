using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of check.run.plan and check.run.execute, and the state both read
/// (docs/tools/eplan_check_run.md, sections 2 and 5).
/// </summary>
internal sealed class CheckRunRequest
{
    private CheckRunRequest(OpenProject project, IReadOnlyList<string>? pages, string? scheme)
    {
        Project = project;
        Pages = pages;
        Scheme = scheme;
    }

    public OpenProject Project { get; }

    /// <summary>The pages' full names as EPLAN has them, in the order given; null for the whole project.</summary>
    public IReadOnlyList<string>? Pages { get; }

    /// <summary>The scheme as given, trimmed; null for the one EPLAN uses now.</summary>
    public string? Scheme { get; }

    /// <summary>Every argument problem at once as INVALID_ARGUMENT, then the project and every missing page.</summary>
    public static CheckRunRequest Check(CheckRunArgs args, IProjectCatalog catalog, IPageReader reader)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        PageScope.Check(args.Pages, violations);
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string? scheme = string.IsNullOrWhiteSpace(args.Scheme) ? null : args.Scheme!.Trim();
        return new CheckRunRequest(project, PageScope.Resolve(args.Pages, project, reader), scheme);
    }

    /// <summary>The page IDs and the message count now, read in the caller's main-thread call.</summary>
    public (IReadOnlyList<string> PageIds, int Messages) ReadState(ICheckRunner runner, IProjectMessageReader messages) =>
        (runner.PageIds(Project.Path, Pages), messages.ReadMessages(Project.Path).Count);

    /// <summary>
    /// The plan's state (tool spec 5): the session identifier of every page checked, and how many check messages the
    /// project has. A page created, removed or replaced in the scope, or a check run meanwhile, changes it.
    /// </summary>
    public static string StateFingerprint(IReadOnlyList<string> pageIds, int messages) =>
        Fingerprint.Of(pageIds.Select(id => "page\t" + id).Append("messages\t" + messages.ToString(CultureInfo.InvariantCulture)));
}
