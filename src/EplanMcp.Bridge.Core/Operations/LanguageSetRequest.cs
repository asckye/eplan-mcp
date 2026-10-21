using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of language.set.plan and language.set.execute, the project's language settings both read, and
/// the settings the change should leave (docs/tools/eplan_language_set.md, sections 2 and 5).
/// </summary>
internal sealed class LanguageSetRequest
{
    private LanguageSetRequest(OpenProject project, LanguageSettings before, IReadOnlyList<string>? display, IReadOnlyList<string> toAdd)
    {
        Project = project;
        Before = before;
        Display = display;
        ToAdd = toAdd;
        Translation = [.. LanguageCodes.Split(before.Translation), .. toAdd];
    }

    public OpenProject Project { get; }

    /// <summary>The settings now, read in the same main-thread call.</summary>
    public LanguageSettings Before { get; }

    /// <summary>The displayed languages to set, in order; <c>["##_##"]</c> clears them; null leaves them.</summary>
    public IReadOnlyList<string>? Display { get; }

    /// <summary>The languages of <c>add</c> the project does not have yet, in the order given.</summary>
    public IReadOnlyList<string> ToAdd { get; }

    /// <summary>The translation languages after the change: those now, then <see cref="ToAdd"/>.</summary>
    public IReadOnlyList<string> Translation { get; }

    /// <summary>True when the displayed languages are to be cleared, which EPLAN's SetDisplayedLanguages cannot do (A28).</summary>
    public bool ClearsDisplay => Display is { Count: 1 } display && display[0] == LanguageCodes.Unset;

    /// <summary>
    /// Every argument problem at once as INVALID_ARGUMENT, then the project, then every displayed language that is
    /// neither a translation language nor added, with the translation languages as <c>metadata.candidates</c>.
    /// </summary>
    public static LanguageSetRequest Check(LanguageSetArgs args, IProjectCatalog catalog, IProjectLanguages languages)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (args.Display is null && args.Add is null)
        {
            violations.Add("Give 'display', 'add' or both.");
        }

        LanguageCodes.Check(args.Display, "display", violations, unsetAlone: true);
        LanguageCodes.Check(args.Add, "add", violations);
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        LanguageSettings before = languages.Read(project.Path);
        List<string> translation = LanguageCodes.Split(before.Translation);
        List<string> toAdd = [.. (args.Add ?? []).Where(code => !translation.Contains(code, StringComparer.Ordinal))];
        List<string>? display = args.Display;
        for (int i = 0; i < (display?.Count ?? 0); i++)
        {
            string code = display![i];
            if (code != LanguageCodes.Unset && !translation.Contains(code, StringComparer.Ordinal) && !toAdd.Contains(code, StringComparer.Ordinal))
            {
                violations.Add($"display[{LanguageCodes.Number(i)}] is not a translation language of the project; add it with 'add', or use one of metadata.candidates.");
            }
        }

        return violations.Count > 0 ? throw LanguageCodes.Invalid(violations, translation) : new LanguageSetRequest(project, before, display, toAdd);
    }

    /// <summary>The displayed languages after the change.</summary>
    public IReadOnlyList<string> DisplayAfter() => Display ?? LanguageCodes.Split(Before.Display);

    /// <summary>
    /// True when the change would leave the settings as they are: nothing to add, and the displayed languages given are
    /// those now, in the same order.
    /// </summary>
    public bool ChangesNothing() =>
        ToAdd.Count == 0 && (Display is null || Display.SequenceEqual(LanguageCodes.Split(Before.Display), StringComparer.Ordinal));

    /// <summary>The plan's state (tool spec 5): both settings as they are stored now.</summary>
    public string StateFingerprint() => Fingerprint.Of(["translation\t" + Before.Translation, "display\t" + Before.Display]);

    /// <summary>
    /// Null when <paramref name="after"/> is what was asked for: exactly the translation languages before and those
    /// added, in any order EPLAN keeps them, and the displayed languages given, in their order. Otherwise what differs,
    /// for the error. Displayed languages that were not given are not compared: EPLAN may change them itself (A28).
    /// </summary>
    public string? Differs(LanguageSettings after)
    {
        var problems = new List<string>();
        List<string> translation = LanguageCodes.Split(after.Translation);
        if (!new HashSet<string>(translation, StringComparer.Ordinal).SetEquals(Translation) || translation.Count != Translation.Count)
        {
            problems.Add("translation languages: asked for " + string.Join(", ", Translation) + ", EPLAN has " + string.Join(", ", translation));
        }

        List<string> display = LanguageCodes.Split(after.Display);
        if (Display is not null && !display.SequenceEqual(Display, StringComparer.Ordinal))
        {
            problems.Add("displayed languages: asked for " + string.Join(", ", Display) + ", EPLAN has " + string.Join(", ", display));
        }

        return problems.Count == 0 ? null : string.Join("; ", problems);
    }
}
