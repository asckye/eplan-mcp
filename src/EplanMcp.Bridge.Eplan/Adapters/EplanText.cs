using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
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
    /// language when it is omitted (M2 design Q3). EPLAN parses the code itself. Languages.GuiLanguage is documented
    /// and confirmed by reflection; its value on a live system is checked by the Large tests.
    /// </summary>
    public static ISOCode.Language Language(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            using var languages = new Languages();
            return languages.GuiLanguage.GetNumber();
        }

        using var iso = new ISOCode(code);
        return iso.IsValid()
            ? iso.GetNumber()
            : throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "'language' is not a language EPLAN knows. Use a code like zh_CN or en_US, or omit it for EPLAN's user interface language.",
                retryable: false,
                new Dictionary<string, string> { ["language"] = code! });
    }

    /// <summary>
    /// The text of a property in <paramref name="language"/>, or null when the property is not set. Never
    /// <c>ToString()</c>: on a multi-language property it returns every language at once (<c>de_DE@…;zh_CN@…;</c>),
    /// while <c>GetStringToDisplay</c> handles multi-language and language-independent values alike (S6).
    /// </summary>
    public static string? Read(PropertyValue value, ISOCode.Language language)
    {
        if (value.IsEmpty)
        {
            return null;
        }

        using MultiLangString text = value.ToMultiLangString();
        string display = text.GetStringToDisplay(language);
        return display.Length == 0 ? null : display;
    }
}
