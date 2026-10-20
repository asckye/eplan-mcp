using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Creates pages, implemented by the EPLAN adapter layer (docs/tools/eplan_page_create.md, section 5). Main thread
/// only; <see cref="Create"/> only inside the write's transaction (<see cref="TransactionRunner"/>).
/// </summary>
public interface IPageWriter
{
    /// <summary>Whether EPLAN knows <paramref name="language"/>, a code like <c>zh_CN</c>.</summary>
    public bool KnowsLanguage(string language);

    /// <summary>
    /// Creates one page of type <paramref name="pageType"/> from the parts of <paramref name="name"/>, writes
    /// <paramref name="description"/> (when not null) as its only text in <paramref name="language"/> (null: EPLAN's
    /// user interface language), and reads the page back: name, type and description in that language. EPLAN drops
    /// name parts the project's page structure does not use (api-notes A13), so the name read back can differ. Throws
    /// ALREADY_EXISTS when EPLAN reports that the name is taken.
    /// </summary>
    public PageSummary Create(string projectPath, PageName name, string pageType, string? description, string? language);
}
