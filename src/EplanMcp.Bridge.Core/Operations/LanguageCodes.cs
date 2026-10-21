using System.Globalization;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Language codes in tool arguments and in a project's language settings (docs/tools/eplan_language_set.md, sections 2
/// and 5; api-notes A28): EPLAN's ISO codes such as <c>de_DE</c>, stored one after another, each ended by <c>;</c>.
/// </summary>
public static class LanguageCodes
{
    /// <summary>The most languages one argument may hold.</summary>
    public const int MaxLanguages = 10;

    /// <summary>What a language setting holds when it is not set (A28: <c>##_##;</c>, <c>##_##</c>).</summary>
    public const string Unset = "##_##";

    // EPLAN's ISO language codes are two lower-case letters, '_' and two upper-case letters (ISOCode.Language).
    private static readonly Regex _code = new("^[a-z]{2}_[A-Z]{2}$", RegexOptions.CultureInvariant);

    /// <summary>The languages a stored setting holds, in stored order; <c>["##_##"]</c> stays as it is.</summary>
    public static List<string> Split(string? stored) =>
        [.. (stored ?? string.Empty).Split(';').Select(code => code.Trim()).Where(code => code.Length > 0)];

    /// <summary>The stored form of <paramref name="languages"/>: each ended by <c>;</c>, as EPLAN stores them (A28).</summary>
    public static string Stored(IEnumerable<string> languages) => string.Concat(languages.Select(code => code + ";"));

    /// <summary>
    /// The argument of <c>SetDisplayedLanguages</c>: the codes separated by commas. EPLAN refuses semicolons, also a
    /// final one, with an ArgumentException (S23 r1, A28).
    /// </summary>
    public static string ForDisplay(IEnumerable<string> languages) => string.Join(",", languages);

    /// <summary>The settings as the tool results show them.</summary>
    public static ProjectLanguages Lists(LanguageSettings settings) => new() { Translation = Split(settings.Translation), Display = Split(settings.Display) };

    /// <summary>
    /// Every problem of the argument <paramref name="name"/> at once, by its index: 1 to <see cref="MaxLanguages"/>
    /// codes of the form <c>xx_YY</c>, each once. With <paramref name="unsetAlone"/>, <c>##_##</c> is allowed as the
    /// only entry. Nothing for an argument that was not given.
    /// </summary>
    public static void Check(IReadOnlyList<string?>? languages, string name, List<string> violations, bool unsetAlone = false)
    {
        if (languages is null)
        {
            return;
        }

        if (languages.Count is < 1 or > MaxLanguages)
        {
            violations.Add($"'{name}' must hold 1 to {Number(MaxLanguages)} language codes such as en_US; omit it to leave it out.");
        }

        for (int i = 0; i < languages.Count && i < MaxLanguages; i++)
        {
            string code = languages[i] ?? string.Empty;
            int same = Enumerable.Range(0, i).Where(j => string.Equals(languages[j], code, StringComparison.Ordinal)).DefaultIfEmpty(-1).First();
            if (unsetAlone && code == Unset)
            {
                if (languages.Count > 1)
                {
                    violations.Add($"{name}[{Number(i)}] is ##_##, which clears the setting and must be the only entry.");
                }
            }
            else if (!_code.IsMatch(code))
            {
                violations.Add($"{name}[{Number(i)}] is not a language code of the form xx_YY, such as en_US or zh_CN.");
            }
            else if (same >= 0)
            {
                violations.Add($"{name}[{Number(i)}] repeats {name}[{Number(same)}].");
            }
        }
    }

    /// <summary>INVALID_ARGUMENT with every problem, and the project's translation languages as <c>metadata.candidates</c>.</summary>
    public static BridgeErrorException Invalid(IReadOnlyList<string> violations, IEnumerable<string> translation)
    {
        BridgeErrorException error = WriteArguments.Invalid(violations);
        error.Error.Metadata!["candidates"] = string.Join("\n", translation);
        return error;
    }

    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
