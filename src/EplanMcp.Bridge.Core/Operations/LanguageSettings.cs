namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A project's language settings as stored (api-notes A28), e.g. <c>de_DE;en_US;</c>; <c>##_##</c> when not set.</summary>
public sealed class LanguageSettings
{
    /// <summary><c>TRANSLATEGUI.TRANSLATE_LANGUAGES</c>.</summary>
    public string Translation { get; set; } = string.Empty;

    /// <summary><c>TRANSLATEGUI.DISPLAYED_LANGUAGES</c>.</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary><c>TRANSLATEGUI.SOURCE_LANGUAGE</c>, one code without a final <c>;</c> (A28: <c>##_##</c>, <c>de_DE</c>).</summary>
    public string Source { get; set; } = string.Empty;
}
