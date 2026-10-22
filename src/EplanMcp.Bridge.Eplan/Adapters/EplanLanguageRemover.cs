using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a project's texts and removes languages the way spike S23 did (api-notes A28): the stored values of every
/// function's <c>FUNC_TEXT</c> and every page's <c>PAGE_NOMINATIOMN</c>, and <c>Translate.RemoveProjectLanguage(project,
/// code)</c>, which deletes the language's translations, takes it out of the translation languages and may change the
/// displayed languages. Main thread, inside the dispatcher's LockingStep; <see cref="Remove"/> inside the write's
/// TransactionScope, whose SafetyPoint S23 saw take the translations back but not the settings. Only the objects this
/// class creates are disposed.
/// </summary>
internal sealed class EplanLanguageRemover : ILanguageRemover
{
    /// <summary>Every text as <see cref="EplanLanguageTexts.All"/> reads it, as S23 read all 497 texts of the sample project.</summary>
    public IReadOnlyList<LanguageText> Texts(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return EplanLanguageTexts.All(finder);
    }

    /// <summary>
    /// Returns what EPLAN returned without judging it: false also after a removal (A28, S24 r18), so the operation reads
    /// the settings back instead; a source language EPLAN refuses with a system message (EPLAN XML docs) stays there.
    /// What it throws (BaseException, ArgumentException) is EPLAN_OPERATION_FAILED; locks and licences stay with the
    /// exception mapper.
    /// </summary>
    public bool Remove(string projectPath, string language)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        try
        {
            using var translate = new Translate();
            return translate.RemoveProjectLanguage(project, language);
        }
        catch (Exception ex) when ((ex is BaseException or ArgumentException) && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN could not remove {language}, so nothing was removed. metadata.eplanMessage has EPLAN's message.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}
