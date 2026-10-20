using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads project text the way spike S6 found works (api-notes A11, coding standards 9.5, rule 13a). The Host cleans
/// and cuts the text afterwards (ResultShaper); here it is returned as EPLAN gives it.
/// </summary>
internal static class EplanText
{
    /// <summary>
    /// The language a tool's <c>language</c> argument names (for example <c>zh_CN</c>), or EPLAN's user interface
    /// language with fallback when it is omitted (M2 design Q3). EPLAN parses the code itself. Languages.GuiLanguage is
    /// documented and confirmed by reflection; its value on a live system is checked by the Large tests.
    /// </summary>
    public static TextLanguage Language(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            using var languages = new Languages();
            return new TextLanguage(languages.GuiLanguage.GetNumber(), fallback: true);
        }

        using var iso = new ISOCode(code);
        return iso.IsValid()
            ? new TextLanguage(iso.GetNumber(), fallback: false)
            : throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "'language' is not a language EPLAN knows. Use a code like zh_CN or en_US, or omit it for EPLAN's user interface language.",
                retryable: false,
                new Dictionary<string, string> { ["language"] = code! });
    }

    /// <summary>The text of a property in <paramref name="language"/>, or null; see <see cref="ReadWithLanguage"/>.</summary>
    public static string? Read(PropertyValue value, TextLanguage language) => ReadWithLanguage(value, language).Text;

    /// <summary>
    /// The text of a property, or null when the property is not set, and the language it was read in when that is a
    /// fallback (null otherwise). The text is never <c>ToString()</c>: on a multi-language property it returns every
    /// language at once (<c>de_DE@…;zh_CN@…;</c>), while <c>GetStringToDisplay</c> handles multi-language and
    /// language-independent values alike (S6). It does not fall back to other languages (A12), so with
    /// <see cref="TextLanguage.Fallback"/> the raw value is read for the languages the text has, only when the requested
    /// one has no text.
    /// </summary>
    public static (string? Text, string? Fallback) ReadWithLanguage(PropertyValue value, TextLanguage language)
    {
        if (value.IsEmpty)
        {
            return (null, null);
        }

        using MultiLangString text = value.ToMultiLangString();
        string display = text.GetStringToDisplay(language.Number);
        if (display.Length > 0 || !language.Fallback)
        {
            return (display.Length == 0 ? null : display, null);
        }

        foreach (string code in TextLanguages.Fallbacks(value.ToString()))
        {
            using var iso = new ISOCode(code);
            if (iso.IsValid() && text.GetStringToDisplay(iso.GetNumber()) is { Length: > 0 } other)
            {
                return (other, code);
            }
        }

        return (null, null);
    }
}

/// <summary>
/// The language to read project texts in. <see cref="Fallback"/> is true when the tool's <c>language</c> was omitted:
/// a text with nothing in EPLAN's user interface language is then read in another language it has
/// (docs/design/2026-10-19-text-language-fallback.md).
/// </summary>
internal sealed class TextLanguage(ISOCode.Language number, bool fallback)
{
    public ISOCode.Language Number { get; } = number;

    public bool Fallback { get; } = fallback;
}
