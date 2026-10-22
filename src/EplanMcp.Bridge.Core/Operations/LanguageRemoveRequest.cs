using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of language.remove.plan and language.remove.execute, the project's language settings and the
/// texts with translations in the languages, which both read the same way (docs/tools/eplan_language_remove.md,
/// sections 2 and 5).
/// </summary>
internal sealed class LanguageRemoveRequest
{
    private LanguageRemoveRequest(OpenProject project, IReadOnlyList<string> languages, LanguageSettings before, IReadOnlyList<LanguageText> affected)
    {
        Project = project;
        Languages = languages;
        Before = before;
        Affected = affected;
    }

    public OpenProject Project { get; }

    /// <summary>The languages to remove, in the order given.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>The settings now, read in the same main-thread call.</summary>
    public LanguageSettings Before { get; }

    /// <summary>The texts with a translation in one of <see cref="Languages"/>.</summary>
    public IReadOnlyList<LanguageText> Affected { get; }

    /// <summary>
    /// Every argument problem at once as INVALID_ARGUMENT, then the project, then every language that is not a
    /// translation language or is the source language, with the translation languages as <c>metadata.candidates</c>.
    /// </summary>
    public static LanguageRemoveRequest Check(LanguageRemoveArgs args, IProjectCatalog catalog, IProjectLanguages languages, ILanguageRemover remover)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<string> codes = args.Languages ?? [];
        LanguageCodes.Check(codes, "languages", violations, required: true);
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        LanguageSettings before = languages.Read(project.Path);
        List<string> translation = LanguageCodes.Split(before.Translation);
        string source = LanguageCodes.SourceOf(before);
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i] == source)
            {
                violations.Add($"languages[{LanguageCodes.Number(i)}] is the project's source language, which EPLAN cannot remove.");
            }
            else if (!translation.Contains(codes[i], StringComparer.Ordinal))
            {
                violations.Add($"languages[{LanguageCodes.Number(i)}] is not a translation language of the project; metadata.candidates lists them.");
            }
        }

        return violations.Count > 0
            ? throw LanguageCodes.Invalid(violations, translation)
            : new LanguageRemoveRequest(project, codes, before, [.. remover.Texts(project.Path).Where(text => codes.Any(code => Has(text, code)))]);
    }

    /// <summary>True when <paramref name="text"/> stores a translation in <paramref name="language"/>.</summary>
    public static bool Has(LanguageText text, string language) => TextLanguages.Stored(text.Raw).Contains(language, StringComparer.Ordinal);

    /// <summary>
    /// The plan's state (tool spec 5): the three settings as stored, and every text the removal touches with its
    /// session identifier. A text changed or a translation added meanwhile changes it; other texts do not.
    /// </summary>
    public string StateFingerprint() => Fingerprint.Of(
    [
        "translation\t" + Before.Translation,
        "display\t" + Before.Display,
        "source\t" + Before.Source,
        .. Affected.Select(text => "text\t" + text.Id + "\t" + text.Raw),
    ]);
}
