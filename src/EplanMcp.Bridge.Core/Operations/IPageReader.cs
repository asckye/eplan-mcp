using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads the pages of one open project, implemented by the EPLAN adapter layer. Called on the main thread only, with
/// a path that <see cref="ProjectResolver"/> chose in the same call.
/// </summary>
public interface IPageReader
{
    /// <summary>The page type names EPLAN knows (its <c>DocumentType</c> enum), for checking <c>page_type</c>.</summary>
    public IReadOnlyCollection<string> PageTypes { get; }

    /// <summary>
    /// The pages in EPLAN's page order, narrowed by EPLAN's own page filter: <paramref name="namePrefix"/> matches the
    /// start of the full name, case-sensitive (S6); <paramref name="pageType"/> is one of <see cref="PageTypes"/>.
    /// Descriptions in <paramref name="language"/>. Throws INVALID_ARGUMENT for a language EPLAN does not know.
    /// </summary>
    public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language);
}
