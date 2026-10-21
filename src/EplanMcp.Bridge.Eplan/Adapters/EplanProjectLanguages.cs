using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads and changes a project's languages the way spike S23 did (api-notes A28): the settings with
/// <c>ProjectSettings.GetStringSetting(node, 0)</c>, the changes with HEServices' <c>Translate</c>,
/// <c>AddProjectLanguage(project, code)</c> and <c>SetDisplayedLanguages(project, "en_US,zh_CN")</c>. Main thread,
/// inside the dispatcher's LockingStep; the changes inside the write's TransactionScope, whose SafetyPoint does not take
/// the settings back (A28), so <see cref="Write"/> also runs after its rollback. Only the objects this class creates are
/// disposed.
/// </summary>
internal sealed class EplanProjectLanguages : IProjectLanguages
{
    private const string TranslationNode = "TRANSLATEGUI.TRANSLATE_LANGUAGES";
    private const string DisplayNode = "TRANSLATEGUI.DISPLAYED_LANGUAGES";
    private const string SourceNode = "TRANSLATEGUI.SOURCE_LANGUAGE";

    public LanguageSettings Read(string projectPath)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        return Run("EPLAN could not read the project's language settings, so nothing was changed.", () =>
        {
            using var settings = new ProjectSettings(project);
            return new LanguageSettings
            {
                Translation = settings.GetStringSetting(TranslationNode, 0) ?? string.Empty,
                Display = settings.GetStringSetting(DisplayNode, 0) ?? string.Empty,
                Source = settings.GetStringSetting(SourceNode, 0) ?? string.Empty,
            };
        });
    }

    /// <summary>True also when the project had the language (EPLAN XML docs); false is taken as a refusal.</summary>
    public void Add(string projectPath, string language)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        string refused = $"EPLAN did not add {language} to the translation languages, so nothing was changed.";
        if (!Run(refused, () =>
        {
            using var translate = new Translate();
            return translate.AddProjectLanguage(project, language);
        }))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, refused + " Ask the engineer to add it in EPLAN's project settings (Translation) to see why.");
        }
    }

    /// <summary>EPLAN throws ArgumentException for a list it does not take, e.g. one with semicolons (S23 r1).</summary>
    public void SetDisplayed(string projectPath, string languages)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        string refused = "EPLAN did not set the displayed languages, so nothing was changed.";
        if (!Run(refused, () =>
        {
            using var translate = new Translate();
            return translate.SetDisplayedLanguages(project, languages);
        }))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, refused + " Ask the engineer to set them in EPLAN's project settings (Translation) to see why.");
        }
    }

    /// <summary>
    /// <c>SetStringSetting(node, value, 0)</c> on both nodes, the values as <see cref="Read"/> returns them. Not
    /// verified by a spike: the Large tests check it by reading back (docs/tools/eplan_language_set.md, section 5). A
    /// settings call shows no dialog, as SchemeSetting in EplanExporter, so no QuietModeStep of its own: it may run
    /// inside the write's, or after its rollback.
    /// </summary>
    public void Write(string projectPath, LanguageSettings settings)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        Run("EPLAN could not write the project's language settings.", () =>
        {
            using var stored = new ProjectSettings(project);
            stored.SetStringSetting(TranslationNode, settings.Translation, 0);
            stored.SetStringSetting(DisplayNode, settings.Display, 0);
            return true;
        });
    }

    /// <summary>
    /// What EPLAN throws when it refuses (EPLAN XML docs: ArgumentException, ApplicationException, BaseException) goes
    /// back as EPLAN_OPERATION_FAILED with EPLAN's message; what the exception mapper knows (locks, a missing licence)
    /// stays for it.
    /// </summary>
    private static T Run<T>(string failed, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when ((ex is BaseException or ArgumentException or ApplicationException) && new EplanExceptionMapper().Map(ex) is null)
        {
            var error = new BridgeErrorException(ErrorReason.EplanOperationFailed, failed + " metadata.eplanMessage has EPLAN's message.", ex);
            error.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw error;
        }
    }
}
