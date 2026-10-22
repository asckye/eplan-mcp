using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of placeholder.apply (docs/tools/eplan_placeholder_apply.md, section 2) and the one placeholder
/// they name: on the page, named as given (any name when none is given), with the value set. None is INVALID_ARGUMENT
/// with the page's placeholders as candidates, more than one AMBIGUOUS_NAME.
/// </summary>
internal sealed class PlaceholderApplyRequest
{
    private PlaceholderApplyRequest(OpenProject project, string page, PlaceholderRecord target, string valueSet)
    {
        Project = project;
        Page = page;
        Target = target;
        ValueSet = valueSet;
    }

    public OpenProject Project { get; }

    public string Page { get; }

    public PlaceholderRecord Target { get; }

    public string ValueSet { get; }

    public static PlaceholderApplyRequest Check(PlaceholderApplyArgs args, IProjectCatalog catalog, IPageReader pages, IPlaceholderReader reader)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (string.IsNullOrEmpty(args.Page))
        {
            violations.Add("'page' is required: the full name of the placeholder's page, as eplan_placeholder_list returns it.");
        }

        if (string.IsNullOrEmpty(args.ValueSet))
        {
            violations.Add("'value_set' is required: one of the placeholder's valueSets from eplan_placeholder_list.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string page = PageScope.Resolve([args.Page!], project, pages)![0];
        // No current values here: outside the write's transaction EPLAN refuses them (api-notes A33).
        PlaceholderRecord[] onPage = [.. PlaceholderListOperation.Sorted(reader.Read(project.Path, null, currentValues: false).Where(p => p.Page == page))];
        PlaceholderRecord[] named = [.. onPage.Where(p => args.Placeholder is null || p.Name == args.Placeholder)];
        PlaceholderRecord[] matching = [.. named.Where(p => p.ValueSets.Contains(args.ValueSet!, StringComparer.Ordinal))];
        return matching.Length switch
        {
            1 => new PlaceholderApplyRequest(project, page, matching[0], args.ValueSet!),
            0 when named.Length == 0 => throw NotFound($"The page has no placeholder{(args.Placeholder is null ? string.Empty : " with that name")}, so nothing was changed; metadata.candidates lists its placeholders. Check with eplan_placeholder_list.", onPage),
            0 => throw NotFound("No placeholder of that name on the page has this value set, so nothing was changed; metadata.candidates lists their value sets (case-sensitive). Check with eplan_placeholder_list.", named),
            _ => throw new BridgeErrorException(
                ErrorReason.AmbiguousName,
                "Several placeholders on the page fit, so nothing was changed. Pass 'placeholder' to choose one; when they share the name and the value set, ask the engineer to apply it in EPLAN.",
                retryable: false,
                Candidates(matching)),
        };
    }

    /// <summary>The placeholder, its values as stored, and the value set: the plan's state (M3 design 4.2).</summary>
    public string Fingerprint() => Operations.Fingerprint.Of(
    [
        "project\t" + Project.Path,
        "placeholder\t" + Page + "\t" + Target.Name + "\t" + ValueSet,
        .. Target.Properties.VariableNames.Select((v, i) => "variable\t" + i.ToString(CultureInfo.InvariantCulture) + "\t" + v),
        .. Target.Values.Select(v => "value\t" + v.ValueSet + "\t" + v.Variable + "\t" + v.Value.Raw),
    ]);

    /// <summary>The value set's value of each variable, in the variables' order.</summary>
    public IEnumerable<PlaceholderValueRecord> Values() => Target.Values.Where(v => v.ValueSet == ValueSet);

    /// <summary>The name as the change list and the forms show it.</summary>
    public string DisplayName => Target.Name.Length == 0 ? Page + " (no name)" : Page + " " + Target.Name;

    private static BridgeErrorException NotFound(string message, IReadOnlyList<PlaceholderRecord> candidates) =>
        new(ErrorReason.InvalidArgument, message, retryable: false, Candidates(candidates));

    /// <summary>At most <see cref="ProjectResolver.MaxCandidates"/> placeholders, one per line: name (or none) and value sets.</summary>
    private static Dictionary<string, string> Candidates(IReadOnlyList<PlaceholderRecord> placeholders) => new()
    {
        ["candidates"] = string.Join("\n", placeholders.Take(ProjectResolver.MaxCandidates)
            .Select(p => (p.Name.Length == 0 ? "(no name)" : "'" + p.Name + "'") + ": " + string.Join(", ", p.ValueSets))),
    };
}
