namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads a project's texts and translates them for eplan_project_translate (docs/tools/eplan_project_translate.md,
/// section 5), implemented by the EPLAN adapter layer; the language settings are read through
/// <see cref="IProjectLanguages"/>. Main thread only; <see cref="Translate"/> only inside the write's transaction.
/// </summary>
public interface IProjectTranslator
{
    /// <summary>
    /// The function texts and page descriptions that are set, as stored with all their languages and named, read as
    /// eplan_language_remove reads them (api-notes A28): of the whole project when <paramref name="pages"/> is null,
    /// otherwise of those pages (full names as EPLAN has them) and the functions on them.
    /// </summary>
    public IReadOnlyList<LanguageText> Texts(string projectPath, IReadOnlyList<string>? pages);

    /// <summary>
    /// <c>Translate.TranslateObjects</c> on the project, or on the pages and the functions on them, which fills in the
    /// project's translation languages from the translation database (S23 r2). Returns what EPLAN returned, for the log:
    /// only the texts read back tell what changed.
    /// </summary>
    public bool Translate(string projectPath, IReadOnlyList<string>? pages);
}
