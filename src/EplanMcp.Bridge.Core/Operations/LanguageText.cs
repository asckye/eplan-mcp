namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A multi-language text as <see cref="ILanguageRemover.Texts"/> reads it. Project data.</summary>
public sealed class LanguageText
{
    /// <summary>What the text belongs to and its session identifier, e.g. <c>F 1/2/3</c> for a function's text.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The text as stored with all its languages, <c>de_DE@…;en_US@…;</c>.</summary>
    public string Raw { get; set; } = string.Empty;
}
