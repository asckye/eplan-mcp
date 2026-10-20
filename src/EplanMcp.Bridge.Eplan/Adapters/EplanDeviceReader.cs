using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

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

    public IReadOnlyList<FunctionName> ReadMainFunctionNames(string projectPath)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        return
        [
            .. finder.GetFunctions(null)
                .Where(f => f.IsMainFunction)
                .Select(f => new FunctionName { Name = f.Name ?? string.Empty, VisibleName = f.VisibleName ?? string.Empty }),
        ];
    }

    public DeviceRecord? ReadDevice(string projectPath, string tag, string? language, bool withProperties)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        Function? main = FindMain(finder, tag);
        if (main is null)
        {
            return null;
        }

        ArticleReference[] references = main.ArticleReferences;
        return new DeviceRecord
        {
            Main = Detail(main, textLanguage, withText: true),
            Auxiliary = [.. main.SubFunctions.Select(f => Detail(f, textLanguage, withText: false))],
            Parts = references.Length == 0 ? [] : Parts(project, references, textLanguage),
            Properties = withProperties ? EplanProperties.Read(main.Properties, textLanguage) : null,
        };
    }

    private static FunctionDetail Detail(Function function, TextLanguage language, bool withText)
    {
        (string? text, string? textLanguage) = withText ? EplanText.ReadWithLanguage(function.Properties.FUNC_TEXT, language) : (null, null);
        return new FunctionDetail
        {
            Name = function.Name ?? string.Empty,
            VisibleName = function.VisibleName ?? string.Empty,
            Category = function.FunctionCategory.ToString(),
            Page = function.Page?.Name,
            Text = text,
            TextLanguage = textLanguage,
            Pins = [.. function.Pins.Select(pin => pin.Name ?? string.Empty)],
        };
    }

    /// <summary>
    /// Part data from the references only: indexed properties such as FUNC_ARTICLE_PARTNR throw when read without an
    /// index (S6). Descriptions come from the project's own parts (Project.Articles, 293 read in 10 ms in S6), never
    /// from the parts database: Article.LoadFromMasterdata would write into the project (BannedSymbols.txt).
    /// </summary>
    private static List<DevicePart> Parts(Project project, ArticleReference[] references, TextLanguage language)
    {
        ILookup<string, Article> articles = project.Articles.ToLookup(a => a.PartNr ?? string.Empty, StringComparer.Ordinal);
        return
        [
            .. references.Select(reference =>
            {
                string partNumber = reference.PartNr ?? string.Empty;
                string variant = reference.VariantNr ?? string.Empty;
                Article? article = articles[partNumber].FirstOrDefault(a => string.Equals(a.VariantNr, variant, StringComparison.Ordinal))
                    ?? articles[partNumber].FirstOrDefault();
                (string? description, string? descriptionLanguage) = article is null
                    ? (null, null)
                    : EplanText.ReadWithLanguage(article.Properties.ARTICLE_DESCR1, language);
                return new DevicePart
                {
                    PartNumber = partNumber,
                    Variant = variant.Length == 0 ? null : variant,
                    Count = reference.Count,
                    Description = description,
                    DescriptionLanguage = descriptionLanguage,
                    Manufacturer = article is null ? null : EplanText.Read(article.Properties.ARTICLE_MANUFACTURER, language),
                };
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
    /// The first main function whose full tag is exactly <paramref name="tag"/>. The name filter finds a function by its
    /// full tag (S6); the ordinal check keeps the match case-sensitive like EPLAN's prefix matching (api-notes A11).
    /// Auxiliary functions share the tag, so only the main one counts.
    /// </summary>
    internal static Function? FindMain(DMObjectsFinder finder, string tag)
    {
        using var filter = new FunctionsFilter();
        filter.Name = tag;
        filter.ExactNameMatching = true;
        return finder.GetFunctions(filter).FirstOrDefault(f => f.IsMainFunction && string.Equals(f.Name, tag, StringComparison.Ordinal));
    }

    /// <summary>
    /// The page with exactly this full name. PagesFilter.Name with ExactNameMatching finds a page by its full name
    /// (S6); the ordinal check keeps the match case-sensitive like EPLAN's prefix matching (api-notes A11).
    /// </summary>
    internal static Page? FindPage(DMObjectsFinder finder, string name)
    {
        using var filter = new PagesFilter();
        filter.Name = name;
        filter.ExactNameMatching = true;
        return finder.GetPages(filter).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }
}
