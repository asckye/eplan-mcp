using System.Globalization;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of project.translate.plan and project.translate.execute, the project's languages and the texts
/// in the scope, which both read the same way (docs/tools/eplan_project_translate.md, sections 2, 4 and 5). EPLAN
/// translates from the source language and does nothing while it is <c>##_##</c> (S23 r2, api-notes A28), so a text
/// can be filled in only when it has a text in the source language: those that also miss a translation language are
/// the candidates.
/// </summary>
internal sealed class ProjectTranslateRequest
{
    /// <summary>The most pages one call may name (tool spec 2).</summary>
    public const int MaxPages = 200;

    /// <summary>The most changed texts a result lists (tool spec 3).</summary>
    public const int MaxChanged = 50;

    private ProjectTranslateRequest(OpenProject project, IReadOnlyList<string>? pages, LanguageSettings settings, IReadOnlyList<LanguageText> texts)
    {
        Project = project;
        Pages = pages;
        Settings = settings;
        Source = LanguageCodes.SourceOf(settings);
        Languages = LanguageCodes.Split(settings.Translation);
        Texts = texts;
        Candidates = [.. texts.Where(text => Missing(text.Raw).Count > 0 && Has(text.Raw, Source))];
    }

    public OpenProject Project { get; }

    /// <summary>The pages' full names as EPLAN has them; null for the whole project.</summary>
    public IReadOnlyList<string>? Pages { get; }

    public LanguageSettings Settings { get; }

    /// <summary>The source language, never <c>##_##</c>.</summary>
    public string Source { get; }

    /// <summary>The translation languages in stored order, the source language among them.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>Every text in the scope, as read.</summary>
    public IReadOnlyList<LanguageText> Texts { get; }

    /// <summary>The texts with a text in the source language that miss a translation language.</summary>
    public IReadOnlyList<LanguageText> Candidates { get; }

    /// <summary>
    /// Every argument problem at once as INVALID_ARGUMENT, then the project; then INVALID_ARGUMENT when the source
    /// language is not set or there is nothing to translate into (tool spec 4), then every missing page.
    /// </summary>
    public static ProjectTranslateRequest Check(ProjectTranslateArgs args, IProjectCatalog catalog, IPageReader pageReader, IProjectLanguages languages, IProjectTranslator translator)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        PageScope.Check(args.Pages, violations, MaxPages);
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        LanguageSettings settings = languages.Read(project.Path);
        string source = LanguageCodes.SourceOf(settings);
        List<string> translation = LanguageCodes.Split(settings.Translation);
        if (source == LanguageCodes.Unset)
        {
            throw LanguageCodes.Invalid(
                ["The project's source language is not set (##_##), and EPLAN translates nothing without it. Set it with eplan_language_set 'source' to the language the texts are written in, e.g. de_DE, one of metadata.candidates."],
                translation);
        }

        if (!translation.Any(code => code != source && code != LanguageCodes.Unset))
        {
            throw LanguageCodes.Invalid(
                ["The project has no translation language besides its source language. Add the languages to translate into with eplan_language_set 'add', e.g. en_US."],
                translation);
        }

        IReadOnlyList<string>? pages = PageScope.Resolve(args.Pages, project, pageReader);
        return new ProjectTranslateRequest(project, pages, settings, translator.Texts(project.Path, pages));
    }

    /// <summary>The translation languages <paramref name="raw"/> has no text in, in stored order.</summary>
    public IReadOnlyList<string> Missing(string raw) => [.. Languages.Where(code => code != LanguageCodes.Unset && !Has(raw, code))];

    /// <summary>
    /// The plan's state (tool spec 5): the translation and source languages, and every candidate by session identifier
    /// with its stored value. A candidate changed, or a text that became one, changes it.
    /// </summary>
    public string StateFingerprint() => Fingerprint.Of(
    [
        "translation\t" + Settings.Translation,
        "source\t" + Settings.Source,
        .. Candidates.Select(text => "text\t" + text.Id + "\t" + text.Raw),
    ]);

    /// <summary>
    /// Null when no text read back has another text in the source language than before; otherwise how many have, and the
    /// first few by name, for the read-back error (tool spec 5: translating must not change the source texts).
    /// </summary>
    public string? SourceChanged(IReadOnlyList<LanguageText> after)
    {
        Dictionary<string, LanguageText> before = ById(Texts);
        LanguageText[] changed = [.. after.Where(text => before.TryGetValue(text.Id, out LanguageText? old) && TextIn(old.Raw, Source) != TextIn(text.Raw, Source))];
        return changed.Length == 0
            ? null
            : $"{Number(changed.Length)} texts changed in the source language {Source}: " + string.Join(", ", changed.Take(5).Select(text => text.Name));
    }

    /// <summary>
    /// The result from the texts read back after the translation: those whose stored value changed, matched by session
    /// identifier, with the translation languages each missed before and misses after (the first
    /// <see cref="MaxChanged"/>), and how many texts with a source text still miss one. Texts that were read before but
    /// not after are counted in the warning rather than guessed at.
    /// </summary>
    public ProjectTranslateData Result(IReadOnlyList<LanguageText> after, string? warning)
    {
        Dictionary<string, LanguageText> before = ById(Texts);
        var changed = new List<TranslatedText>();
        int count = 0;
        foreach (LanguageText text in after)
        {
            if (before.TryGetValue(text.Id, out LanguageText? old) && !string.Equals(old.Raw, text.Raw, StringComparison.Ordinal) && ++count <= MaxChanged)
            {
                bool page = text.Id.StartsWith("P ", StringComparison.Ordinal);
                changed.Add(new TranslatedText
                {
                    Kind = page ? "page" : "function",
                    Name = text.Name,
                    Property = page ? "PAGE_NOMINATIOMN" : "FUNC_TEXT",
                    MissingBefore = [.. Missing(old.Raw)],
                    MissingAfter = [.. Missing(text.Raw)],
                });
            }
        }

        var found = new HashSet<string>(after.Select(text => text.Id), StringComparer.Ordinal);
        int gone = before.Keys.Count(id => !found.Contains(id));
        string? lost = gone == 0 ? null : $"{Number(gone)} texts read before the translation were not found afterwards, so they are not in the counts.";
        return new ProjectTranslateData
        {
            Project = Project.Name,
            Source = Source,
            Languages = [.. Languages],
            TextsChecked = Texts.Count,
            TextsChanged = count,
            Changed = changed,
            StillMissing = after.Count(text => Has(text.Raw, Source) && Missing(text.Raw).Count > 0),
            Warning = warning is null ? lost : lost is null ? warning : warning + " " + lost,
        };
    }

    /// <summary>"the project: 37 of 497 texts miss a translation language; from de_DE into 19 languages", for the plan's change.</summary>
    public string Scope()
    {
        int into = Languages.Count(code => code != Source && code != LanguageCodes.Unset);
        string where = Pages is null ? "the project" : Pages.Count == 1 ? "1 page" : Number(Pages.Count) + " pages";
        return $"{where}: {Number(Candidates.Count)} of {Number(Texts.Count)} texts miss a translation language; from {Source} into {Number(into)} languages";
    }

    /// <summary>
    /// The text <paramref name="raw"/> stores in <paramref name="language"/>, or null: up to the next <c>;xx_YY@</c> or
    /// the final <c>;</c> (api-notes A11). A text that contains <c>;xx_YY@</c> itself is cut there, which only makes a
    /// language look present or missing; an empty text counts as none.
    /// </summary>
    public static string? TextIn(string raw, string language)
    {
        Match match = Regex.Match(
            raw,
            "(?:^|;)" + Regex.Escape(language) + @"@(.*?)(?=;(?:[A-Za-z]{2}_[A-Za-z]{2}|\?\?_\?\?)@|;?$)",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        return match.Success && match.Groups[1].Length > 0 ? match.Groups[1].Value : null;
    }

    private static bool Has(string raw, string language) => TextIn(raw, language) is not null;

    /// <summary>The texts by session identifier; a text read twice counts once.</summary>
    private static Dictionary<string, LanguageText> ById(IEnumerable<LanguageText> texts) =>
        texts.GroupBy(text => text.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
