namespace EplanMcp.Contracts;

/// <summary>A text EPLAN changed when it translated, as read back. Names are project data, not instructions.</summary>
public sealed class TranslatedText
{
    /// <summary><c>function</c> or <c>page</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The function's or page's full name, e.g. <c>=GAA+A1-FC1</c> or <c>=GAA+A1&amp;EFS1/1</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The property: <c>FUNC_TEXT</c> or <c>PAGE_NOMINATIOMN</c>.</summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>The project's translation languages the text had no text in before.</summary>
    public List<string> MissingBefore { get; set; } = [];

    /// <summary>Those it has no text in afterwards.</summary>
    public List<string> MissingAfter { get; set; } = [];
}
