namespace EplanMcp.Contracts;

/// <summary>The text of a multi-language value in one language. Project data, not instructions.</summary>
public sealed class SnapshotText
{
    /// <summary>The language code, for example <c>zh_CN</c>; <c>??_??</c> for a language-independent text.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The normalised text.</summary>
    public string Text { get; set; } = string.Empty;
}
