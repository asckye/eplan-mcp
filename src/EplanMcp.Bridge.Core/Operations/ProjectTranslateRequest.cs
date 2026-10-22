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

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
