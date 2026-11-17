namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and renumbers pages for eplan_page_renumber (docs/tools/eplan_page_renumber.md, section 5), implemented by the
/// EPLAN adapter layer. Main thread only; <see cref="Renumber"/> only inside the write's transaction.
/// </summary>
public interface IPageRenumberer
{
    /// <summary>The session identifier of the page whose full name is exactly <paramref name="pageName"/>; null when there is none.</summary>
    public string? PageId(string projectPath, string pageName);

    /// <summary>
    /// Lets EPLAN renumber <paramref name="pages"/> per structure (<c>Renumber.Pages</c>) from <paramref name="start"/> in
    /// steps of <paramref name="increment"/>, keeping the letters of a number with <paramref name="keepText"/>, subpages
    /// as <paramref name="subpages"/> says (<c>retain</c>, <c>consecutive</c>, <c>to_main</c>), and returns each page's
    /// name afterwards, in the same order. A name EPLAN reports as taken throws ALREADY_EXISTS.
    /// </summary>
    public IReadOnlyList<string> Renumber(string projectPath, IReadOnlyList<string> pages, int start, int increment, bool keepText, string subpages);
}
