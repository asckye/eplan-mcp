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
    /// <summary>
    /// The texts as stored (<c>de_DE@…;sv_SE@…;</c>, A11), keyed by kind and session identifier; a property that is not
    /// set is left out, since <c>ToString()</c> on it throws (coding standards 9.5, rule 13). S23 read all 497 texts of
    /// the sample project this way.
    /// </summary>
    public IReadOnlyList<LanguageText> Texts(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        var texts = new List<LanguageText>();
        foreach (Function function in finder.GetFunctions(null))
        {
            Add(texts, "F " + function.ToStringIdentifier(), function.Properties.FUNC_TEXT);
        }

        foreach (Page page in finder.GetPages(null))
        {
            Add(texts, "P " + page.ToStringIdentifier(), page.Properties.PAGE_NOMINATIOMN);
        }

        return texts;
    }

    /// <summary>
    /// EPLAN refuses the source language with a system message (EPLAN XML docs), which shows as false here; false and what
    /// it throws (BaseException, ArgumentException) are EPLAN_OPERATION_FAILED; locks and licences stay with the
    /// exception mapper.
    /// </summary>
    public void Remove(string projectPath, string language)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        bool removed;
        try
        {
            using var translate = new Translate();
            removed = translate.RemoveProjectLanguage(project, language);
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

        if (!removed)
        {
            throw new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN did not remove {language}, so nothing was removed. eplan_message_list may show why.");
        }
    }

    private static void Add(List<LanguageText> texts, string id, PropertyValue value)
    {
        if (!value.IsEmpty)
        {
            texts.Add(new LanguageText { Id = id, Raw = value.ToString() ?? string.Empty });
        }
    }
}
