namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and changes a project's languages for eplan_language_set (docs/tools/eplan_language_set.md, section 5),
/// implemented by the EPLAN adapter layer. Main thread only; the changes only inside the write's transaction, except
/// <see cref="Write"/>, which also writes the settings back after a rollback: SafetyPoint does not take project
/// settings back (api-notes A28). EPLAN refusing a change is EPLAN_OPERATION_FAILED.
/// </summary>
public interface IProjectLanguages
{
    /// <summary>The language settings as the project stores them.</summary>
    public LanguageSettings Read(string projectPath);

    /// <summary>Adds <paramref name="language"/> to the translation languages (<c>Translate.AddProjectLanguage</c>).</summary>
    public void Add(string projectPath, string language);

    /// <summary>
    /// Sets the displayed languages (<c>Translate.SetDisplayedLanguages</c>) to <paramref name="languages"/>, separated
    /// by commas as <see cref="LanguageCodes.ForDisplay"/> joins them: EPLAN refuses semicolons (A28).
    /// </summary>
    public void SetDisplayed(string projectPath, string languages);

    /// <summary>
    /// Writes <see cref="LanguageSettings.Translation"/> and <see cref="LanguageSettings.Display"/> into the project's
    /// settings as they are given, in the stored form (<c>en_US;zh_CN;</c>).
    /// </summary>
    public void Write(string projectPath, LanguageSettings settings);
}
