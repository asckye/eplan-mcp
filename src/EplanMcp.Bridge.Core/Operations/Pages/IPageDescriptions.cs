namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and writes page descriptions (<c>PAGE_NOMINATIOMN</c>), implemented by the EPLAN adapter layer
/// (docs/tools/eplan_page_update.md, section 5). Main thread only; <see cref="Write"/> only inside the write's
/// transaction. Pages are named exactly as EPLAN names them.
/// </summary>
public interface IPageDescriptions
{
    /// <summary>
    /// The code EPLAN gives <paramref name="language"/> (for example <c>zh_CN</c>), or EPLAN's user interface language's
    /// when it is null; null when EPLAN does not know the language.
    /// </summary>
    public string? LanguageCode(string? language);

    /// <summary>The page's description: the raw value and, for each language it stores, the text (<c>GetString</c>).</summary>
    public PageDescription Read(string projectPath, string pageName);

    /// <summary>
    /// Replaces the whole description with <paramref name="entries"/>, one <c>AddString</c> each in this order (a
    /// one-language value replaces every language, api-notes A13), and reads it back.
    /// </summary>
    public PageDescription Write(string projectPath, string pageName, IReadOnlyList<TextEntry> entries);
}
