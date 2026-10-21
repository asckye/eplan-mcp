namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What an export of a whole project makes: one file (docs/tools/eplan_export_pxf.md, eplan_export_parts.md, eplan_export_labels.md, eplan_export_untranslated.md).</summary>
public enum ProjectExportKind
{
    Pxf,
    PartsList,
    Labels,
    Untranslated,
}

/// <summary>
/// Exports a whole project into one file, implemented by the EPLAN adapter layer (the tool specs, section 5; api-notes
/// A25). Main thread only. Files are full paths in the folder the Host created. A failed export throws
/// EPLAN_OPERATION_FAILED.
/// </summary>
public interface IProjectExporter
{
    /// <summary>
    /// Exports the project, connections included, as one EPJ (PXF) file; <paramref name="file"/> has no extension,
    /// which EPLAN adds (S25). <paramref name="includeMasterData"/> adds the project's master data.
    /// </summary>
    public void ExportPxf(string projectPath, string file, bool includeMasterData);

    /// <summary>Exports the project's parts list as XML to <paramref name="file"/>, which ends with <c>.xml</c> (S25).</summary>
    public void ExportPartsList(string projectPath, string file);

    /// <summary>
    /// The labelling schemes (<c>USER.Labelling.Config</c>, A24) in EPLAN's order with their output format; reading
    /// them leaves the last used one as it is (EPLAN XML docs of <c>SetScheme</c>).
    /// </summary>
    public IReadOnlyList<LabelScheme> LabelSchemes();

    /// <summary>
    /// The code EPLAN gives <paramref name="language"/> (for example <c>zh_CN</c>), or EPLAN's user interface language's
    /// when it is null; null when EPLAN does not know the language.
    /// </summary>
    public string? LanguageCode(string? language);

    /// <summary>
    /// Exports labels of the project with <paramref name="scheme"/>, texts in <paramref name="language"/>, to
    /// <paramref name="file"/>, whose extension fits the scheme's output format (EPLAN XML docs).
    /// </summary>
    public void ExportLabels(string projectPath, string scheme, string language, string file);

    /// <summary>The project's translation languages, e.g. <c>de_DE</c>, in the order the project keeps them.</summary>
    public IReadOnlyList<string> TranslationLanguages(string projectPath);

    /// <summary>
    /// Exports the project's texts that have no translation in the translation database for <paramref name="languages"/>
    /// to <paramref name="file"/>, tab-separated Unicode text (A25). Reads the translation database only.
    /// </summary>
    public void ExportUntranslated(string projectPath, IReadOnlyList<string> languages, string file);
}
