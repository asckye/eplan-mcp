namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only adapter seam for page free texts (api-notes A49); never retains native objects across calls.</summary>
public interface IPageTextReader
{
    /// <summary>Canonical EPLAN language code, or null when unknown; no interface-language fallback.</summary>
    public string? LanguageCode(string language);

    /// <summary>
    /// Exact page or null if absent. Returns only exact Graphics.Text objects of page origin, including groups;
    /// excludes plot-frame descendants and other text types. Throws on incomplete traversal or language reads.
    /// </summary>
    public PageTextSnapshot? Read(string projectPath, string pageName);
}
