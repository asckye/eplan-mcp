using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads pages. Main thread, inside the dispatcher's LockingStep. Never uses <c>Project.Filter</c> or
/// <c>Page.Filter</c>, which are shared (BannedSymbols.txt); every query has its own <see cref="DMObjectsFinder"/>.
/// </summary>
internal sealed class EplanPageReader : IPageReader
{
    /// <summary>
    /// Undefined (0) is left out: no page has it, and S6 got every page with a prefix from a filter whose page type
    /// was never set, so filtering by it would probably not filter at all.
    /// </summary>
    private static readonly string[] _pageTypes =
    [
        .. Enum.GetNames(typeof(DocumentTypeManager.DocumentType)).Where(name => name != nameof(DocumentTypeManager.DocumentType.Undefined)),
    ];

    public IReadOnlyCollection<string> PageTypes => _pageTypes;

    public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new PagesFilter();
        if (namePrefix is not null)
        {
            filter.Name = namePrefix; // prefix of Page.Name, case-sensitive (S6)
        }

        if (pageType is not null)
        {
            filter.DocumentType = (DocumentTypeManager.DocumentType)Enum.Parse(typeof(DocumentTypeManager.DocumentType), pageType);
        }

        // Without conditions, null: S6 verified that a null filter returns every page, in EPLAN's page order.
        Page[] pages = finder.GetPages(namePrefix is null && pageType is null ? null : filter);
        return
        [
            .. pages.Select(page =>
            {
                (string? description, string? descriptionLanguage) = Description(page, textLanguage);
                return new PageSummary { Name = page.Name, Type = page.PageType.ToString(), Description = description, DescriptionLanguage = descriptionLanguage };
            }),
        ];
    }

    public IReadOnlyList<string> PageNames(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. finder.GetPages(null).Select(page => page.Name)];
    }

    public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var pageFilter = new PagesFilter { Name = pageName, ExactNameMatching = true }; // S6: one page for its exact name
        // The ordinal check keeps the match exact even if EPLAN's exact matching ignores case, which S6 did not test;
        // the operation retries ignoring case on its own terms.
        Page? page = finder.GetPages(pageFilter).FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
        if (page is null)
        {
            return null;
        }

        // FunctionsFilter.Page instead of Page.Functions: the latter depends on the shared Page.Filter (banned). S6 found
        // this filter correct and fast (0 ms on a circuit page).
        using var functionFilter = new FunctionsFilter { Page = page };
        (string? description, string? descriptionLanguage) = Description(page, textLanguage);
        return new PageContent
        {
            Page = new PageDetail
            {
                Name = page.Name,
                Type = page.PageType.ToString(),
                TypeName = page.PageTypeName is { Length: > 0 } typeName ? typeName : null,
                Description = description,
                DescriptionLanguage = descriptionLanguage,
            },
            Functions =
            [
                .. finder.GetFunctions(functionFilter).Select(function => new FunctionRecord
                {
                    Name = function.Name ?? string.Empty,
                    VisibleName = function.VisibleName ?? string.Empty,
                    IsMain = function.IsMainFunction,
                    Category = function.FunctionCategory.ToString(),
                    Page = page.Name,
                    Parts = function.ArticleReferences.Length,
                }),
            ],
            Properties = includeProperties ? EplanProperties.Read(page.Properties, textLanguage) : null,
        };
    }

    /// <summary>
    /// The page description, <c>PAGE_NOMINATIOMN</c> (EPLAN's spelling), a multi-language property (S6), and the
    /// language it was read in when that is a fallback.
    /// </summary>
    internal static (string? Text, string? Fallback) Description(Page page, TextLanguage language) =>
        EplanText.ReadWithLanguage(page.Properties.PAGE_NOMINATIOMN, language);
}
