namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads a project's texts and removes languages for eplan_language_remove (docs/tools/eplan_language_remove.md,
/// section 5), implemented by the EPLAN adapter layer; the settings are read and written back through
/// <see cref="IProjectLanguages"/>. Main thread only; <see cref="Remove"/> only inside the write's transaction. EPLAN
/// refusing is EPLAN_OPERATION_FAILED.
/// </summary>
public interface ILanguageRemover
{
    /// <summary>
    /// Every function text and page description that is set, as stored with all its languages (<c>de_DE@…;sv_SE@…;</c>),
    /// the texts spike S23 counted (api-notes A28).
    /// </summary>
    public IReadOnlyList<LanguageText> Texts(string projectPath);

    /// <summary>
    /// Removes <paramref name="language"/> from the translation languages with its translations in every text
    /// (<c>Translate.RemoveProjectLanguage</c>); EPLAN may change the displayed languages as well (A28).
    /// </summary>
    public void Remove(string projectPath, string language);
}
