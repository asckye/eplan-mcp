using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Turns a tool's optional <c>project</c> argument into one open project (M2 design 4.2). Omitted means the project
/// selected in EPLAN, or the only open one; otherwise the value is a path as eplan_project_list returns it (with or
/// without ".elk") or a project name. Pure logic over <see cref="IProjectCatalog.ListOpenProjects"/>, so an adapter
/// only has to fetch the chosen project by its path; never the "current" project in general (api-notes A11).
/// </summary>
public static class ProjectResolver
{
    /// <summary>The most candidates an <see cref="ErrorReason.AmbiguousName"/> error lists (mcp-tool-design 6.3).</summary>
    public const int MaxCandidates = 20;

    public static OpenProject Resolve(IReadOnlyList<OpenProject> open, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return open.FirstOrDefault(p => p.Current)
                ?? (open.Count == 1 ? open[0] : null)
                ?? throw (open.Count == 0
                    ? NotOpen(null, "No project is open in EPLAN. Ask the engineer to open one.")
                    : Ambiguous(
                        "project",
                        string.Empty,
                        open.Select(p => p.Path),
                        "No project is selected in EPLAN and several are open; pass one of the candidates as 'project'."));
        }

        string wanted = requested!.Trim();
        OpenProject? byPath = open.FirstOrDefault(p => SamePath(p.Path, wanted));
        if (byPath is not null)
        {
            return byPath;
        }

        OpenProject[] byName = [.. open.Where(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase))];
        return byName.Length switch
        {
            1 => byName[0],
            0 => throw NotOpen(wanted, "No open project has this name or path. Call eplan_project_list to see the open projects."),
            _ => throw Ambiguous(
                "project",
                wanted,
                byName.Select(p => p.Path),
                "Several open projects have this name; pass the 'path' of one of the candidates as 'project'."),
        };
    }

    /// <summary>
    /// The <see cref="ErrorReason.AmbiguousName"/> error of every lookup by name. Candidates go into metadata, one
    /// per line, because they are project data; the message only says what to do next (mcp-tool-design 6.2).
    /// </summary>
    public static BridgeErrorException Ambiguous(string kind, string name, IEnumerable<string> candidates, string message)
    {
        string[] all = [.. candidates];
        var metadata = new Dictionary<string, string>
        {
            ["kind"] = kind,
            ["name"] = name,
            ["candidates"] = string.Join("\n", all.Take(MaxCandidates)),
            ["candidateCount"] = all.Length.ToString(CultureInfo.InvariantCulture),
        };
        return new BridgeErrorException(ErrorReason.AmbiguousName, message, retryable: false, metadata);
    }

    private static BridgeErrorException NotOpen(string? project, string message) =>
        new(ErrorReason.ProjectNotOpen, message, retryable: false, project is null ? null : new Dictionary<string, string> { ["project"] = project });

    /// <summary>
    /// EPLAN reports a project path without ".elk" (api-notes A10); engineers and models may add it, write forward
    /// slashes or change the case. No Path.GetFullPath: a relative value must not resolve against EPLAN's directory.
    /// </summary>
    private static bool SamePath(string projectPath, string value) =>
        string.Equals(NormalisePath(projectPath), NormalisePath(value), StringComparison.OrdinalIgnoreCase);

    private static string NormalisePath(string path)
    {
        string p = path.Replace('/', '\\').TrimEnd('\\');
        return p.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 4) : p;
    }
}
