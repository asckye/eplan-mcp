using System.Text.RegularExpressions;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Argument rules shared by the read tools. Every check adds to the caller's list instead of returning early, so a
/// tool reports all problems at once (mcp-tool-design 6.2). EPLAN-specific checks (does EPLAN know this language or
/// page type) happen in the Bridge.
/// </summary>
internal static partial class ToolArguments
{
    public const string Concise = "concise";
    public const string Detailed = "detailed";

    /// <summary>
    /// Shared parameter descriptions, short because they count against each tool's description budget, where the
    /// schema JSON escapes <c>'</c> and <c>+</c> into six characters each.
    /// </summary>
    public const string ProjectDescription = "Name or path; default: the current project.";
    public const string LanguageDescription = "Text language, e.g. zh_CN; default: EPLAN UI";

    /// <summary>An empty string means the same as an omitted optional argument; models send both.</summary>
    public static string? Optional(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Whether <c>response_format</c> asks for detail; an explicit null is the default.</summary>
    public static bool IsDetailed(string? responseFormat, List<string> violations)
    {
        if (responseFormat is null or Concise)
        {
            return false;
        }

        if (responseFormat == Detailed)
        {
            return true;
        }

        violations.Add($"'response_format' must be '{Concise}' or '{Detailed}'.");
        return false;
    }

    /// <summary>
    /// The shape of an EPLAN language code (<c>ISOCode</c>: language_COUNTRY, e.g. zh_CN); whether EPLAN knows it is
    /// checked by the Bridge.
    /// </summary>
    public static void CheckLanguage(string? language, List<string> violations)
    {
        if (Optional(language) is { } code && !LanguageCode().IsMatch(code))
        {
            violations.Add("'language' must be a language code like zh_CN or en_US; omit it for EPLAN's user interface language.");
        }
    }

    [GeneratedRegex("^[A-Za-z]{2}_[A-Za-z]{2}$")]
    private static partial Regex LanguageCode();
}
