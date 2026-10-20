namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One language of a multi-language value: its code as EPLAN stores it (<c>zh_CN</c>, or <c>??_??</c> for the
/// language-independent text, api-notes A11) and its text.
/// </summary>
public sealed class TextEntry(string language, string text)
{
    public string Language { get; } = language;

    public string Text { get; } = text;
}
