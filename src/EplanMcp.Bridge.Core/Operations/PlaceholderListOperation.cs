using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PlaceholderList"/> (docs/tools/eplan_placeholder_list.md): resolves the project and the
/// page, reads every placeholder in the same main-thread call, keeps those on the page, sorts them by page and name
/// (EPLAN's order for the same page and name) and slices them. The values are returned only when detailed. Reads only.
/// </summary>
public sealed class PlaceholderListOperation(IProjectCatalog catalog, IPageReader pages, IPlaceholderReader reader) : Operation<PlaceholderListArgs, PlaceholderListData>
{
    public override string Name => OperationNames.PlaceholderList;

    /// <summary>By page and then name, ordinal; stable, so placeholders with the same page and name keep EPLAN's order.</summary>
    public static IEnumerable<PlaceholderRecord> Sorted(IEnumerable<PlaceholderRecord> placeholders) =>
        placeholders.OrderBy(p => p.Page ?? string.Empty, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal);

    /// <summary>A placeholder as the tool lists it; the value sets' values and the current values only when <paramref name="detailed"/>.</summary>
    public static PlaceholderItem Item(PlaceholderRecord placeholder, bool detailed) => new()
    {
        Name = placeholder.Name,
        Page = placeholder.Page,
        ValueSets = placeholder.ValueSets,
        Variables = placeholder.Properties.VariableNames,
        AssignedObjects = placeholder.AssignedObjects,
        Values = detailed ? [.. placeholder.Values.Select(v => Value(v.Value, v.ValueSet, v.Variable))] : null,
        CurrentValues = detailed ? [.. placeholder.CurrentValues.Select(c => Value(c, null, null))] : null,
    };

    /// <summary>A value as the tools return it: its text and fallback language, never the raw value.</summary>
    public static PlaceholderValue Value(PlaceholderText text, string? valueSet, string? variable) =>
        new() { ValueSet = valueSet, Variable = variable, Text = text.Text, TextLanguage = text.Language };

    protected override PlaceholderListData Execute(PlaceholderListArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string? page = args.Page is null ? null : PageScope.Resolve([args.Page], project, pages)![0];
        List<PlaceholderRecord> placeholders = [.. Sorted(reader.Read(project.Path, args.Language).Where(p => page is null || p.Page == page))];
        return new PlaceholderListData
        {
            Project = project.Name,
            Page = page,
            Placeholders = [.. Paging.Slice(placeholders, args).Select(p => Item(p, args.Detailed))],
            Total = placeholders.Count,
        };
    }
}
