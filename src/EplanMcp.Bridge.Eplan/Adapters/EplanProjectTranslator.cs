using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a project's texts as <see cref="EplanLanguageTexts"/> does and translates them with HEServices'
/// <c>Translate.TranslateObjects</c>, which fills in the project's translation languages from the translation
/// database once the source language is set (S23 r2, api-notes A28). For the whole project it gets the project, as S23
/// translated it; for pages, the pages and the functions on them (the EPLAN XML docs say a page's elements are
/// translated with it; the functions are passed as well, as the spec says). Main thread, inside the dispatcher's
/// LockingStep; <see cref="Translate"/> inside the write's TransactionScope, whose SafetyPoint S23 r2 saw take the
/// texts back. Only the objects this class creates are disposed.
/// </summary>
internal sealed class EplanProjectTranslator : IProjectTranslator
{
    public IReadOnlyList<LanguageText> Texts(string projectPath, IReadOnlyList<string>? pages)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return pages is null ? EplanLanguageTexts.All(finder) : EplanLanguageTexts.OnPages(finder, Find(finder, pages));
    }

    /// <summary>
    /// Returns what EPLAN returned without judging it, as for RemoveProjectLanguage (A28): the texts read back tell.
    /// What it throws (BaseException, EPLAN XML docs; ArgumentException) is EPLAN_OPERATION_FAILED; locks and licences
    /// stay with the exception mapper.
    /// </summary>
    public bool Translate(string projectPath, IReadOnlyList<string>? pages)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        try
        {
            using var finder = new DMObjectsFinder(project);
            Page[]? scope = pages is null ? null : Find(finder, pages);
            StorableObject[] objects = scope is null ? [project] : [.. scope, .. scope.SelectMany(page => EplanLanguageTexts.FunctionsOn(finder, page))];
            using var translate = new Translate();
            return translate.TranslateObjects(objects);
        }
        catch (Exception ex) when ((ex is BaseException or ArgumentException) && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not translate, so nothing was translated. metadata.eplanMessage has EPLAN's message.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }

    /// <summary>The pages the operation resolved in the same main-thread call (PageScope), exactly by name.</summary>
    private static Page[] Find(DMObjectsFinder finder, IReadOnlyList<string> pages) =>
        [.. pages.Select(name => EplanDeviceReader.FindPage(finder, name) ?? throw new InvalidOperationException("A page the operation resolved is gone."))];
}
