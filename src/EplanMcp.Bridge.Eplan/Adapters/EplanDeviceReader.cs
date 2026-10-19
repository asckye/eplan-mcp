using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads functions (devices) into plain records for the device operations. Main thread, inside the dispatcher's
/// LockingStep; the project is found by path (api-notes A11). Spike S6 measured fetching all 2876 functions of the
/// sample project at 14–64 ms and reading their names at 18 ms, so filters other than page and category run in
/// Bridge.Core. Only the objects this class creates are disposed, as in the rest of the adapter layer.
/// </summary>
internal sealed class EplanDeviceReader : IDeviceReader
{
    public bool IsCategory(string category) => TryParseCategory(category, out _);

    public IReadOnlyList<FunctionRecord>? ReadFunctions(string projectPath, string? page, string? category)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter();
        if (page is not null)
        {
            Page? target = FindPage(finder, page);
            if (target is null)
            {
                return null;
            }

            filter.Page = target;
        }

        if (category is not null)
        {
            filter.FunctionCategory = TryParseCategory(category, out FunctionCategory parsed)
                ? parsed
                : throw new ArgumentException("The operation passes checked categories only.", nameof(category));
        }

        // A null filter returns every function (S6); FunctionsFilter.Page and FunctionCategory narrow it (S6).
        Function[] functions = finder.GetFunctions(page is null && category is null ? null : filter);
        return
        [
            .. functions.Select(f => new FunctionRecord
            {
                Name = f.Name ?? string.Empty,
                VisibleName = f.VisibleName ?? string.Empty,
                IsMain = f.IsMainFunction,
                Category = f.FunctionCategory.ToString(),
                Page = f.Page?.Name,
                Parts = f.ArticleReferences.Length,
            }),
        ];
    }

    /// <summary>
    /// Category names only: Enum.TryParse also accepts numbers ("200") and comma lists ("Coil, Motor"), which a
    /// model should not be able to pass as a category.
    /// </summary>
    private static bool TryParseCategory(string name, out FunctionCategory category)
    {
        category = default;
        return name.Length > 0
            && char.IsLetter(name[0])
            && name.IndexOf(',') < 0
            && Enum.TryParse(name, ignoreCase: true, out category)
            && Enum.IsDefined(typeof(FunctionCategory), category);
    }

    /// <summary>
    /// The page with exactly this full name. PagesFilter.Name with ExactNameMatching finds a page by its full name
    /// (S6); the ordinal check keeps the match case-sensitive like EPLAN's prefix matching (api-notes A11).
    /// </summary>
    private static Page? FindPage(DMObjectsFinder finder, string name)
    {
        using var filter = new PagesFilter();
        filter.Name = name;
        filter.ExactNameMatching = true;
        return finder.GetPages(filter).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }
}
