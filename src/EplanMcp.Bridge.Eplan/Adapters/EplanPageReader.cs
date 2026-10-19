using Eplan.EplApi.Base;
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
        ISOCode.Language textLanguage = EplanText.Language(language);
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
        return [.. pages.Select(page => new PageSummary { Name = page.Name, Type = page.PageType.ToString(), Description = Description(page, textLanguage) })];
    }

    /// <summary>The page description, <c>PAGE_NOMINATIOMN</c> (EPLAN's spelling), a multi-language property (S6).</summary>
    internal static string? Description(Page page, ISOCode.Language language) => EplanText.Read(page.Properties.PAGE_NOMINATIOMN, language);
}
