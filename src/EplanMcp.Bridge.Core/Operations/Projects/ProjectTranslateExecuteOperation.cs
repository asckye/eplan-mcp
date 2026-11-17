using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectTranslateExecute"/>: checks the arguments and the plan's fingerprint again, backs the
/// project up (<see cref="ProjectBackups"/>), then in one transaction lets EPLAN translate the scope and reads its texts
/// back (docs/tools/eplan_project_translate.md, section 5). A text whose source-language text changed is a read-back
/// that differs: EPLAN_OPERATION_FAILED, rolled back. What TranslateObjects returns only goes to the log. Translating
/// leaves the language settings as they are (S23 r2, api-notes A28), so, unlike the language tools, nothing is written
/// back after a rollback.
/// </summary>
public sealed class ProjectTranslateExecuteOperation(
    IProjectCatalog catalog,
    IPageReader pages,
    IProjectLanguages languages,
    IProjectTranslator translator,
    IProjectBackup backup,
    ITransactionScopeFactory transactions,
    IBridgeLog log)
    : Operation<ProjectTranslateArgs, ProjectTranslateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_project_translate".</summary>
    public const string Tool = "eplan_project_translate";

    public override string Name => OperationNames.ProjectTranslateExecute;

    protected override ProjectTranslateData Execute(ProjectTranslateArgs args)
    {
        ProjectTranslateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one project.translate.plan returned.");
            }

            request = ProjectTranslateRequest.Check(args, catalog, pages, languages, translator);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The project's languages or texts to translate changed after the plan was made, so nothing was translated. Call eplan_project_translate again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        string path = request.Project.Path;
        Committed<IReadOnlyList<LanguageText>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            OperationContext context = OperationContext.Current;
            context.ThrowIfCancellationRequested();
            bool returned = translator.Translate(path, request.Pages);
            IReadOnlyList<LanguageText> after = translator.Texts(path, request.Pages);
            log.Write(BridgeLogLevel.Information, $"[{context.RequestId}] TranslateObjects returned {returned}; read back {after.Count} texts");
            return request.SourceChanged(after) is { } problem
                ? throw new BridgeErrorException(
                    ErrorReason.EplanOperationFailed,
                    "EPLAN changed texts in the source language while translating, so the translation was rolled back. metadata.problem names them; ask the engineer to check the project's source language and the translation database.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name, ["problem"] = problem })
                : after;
        });
        return request.Result(committed.Result, committed.Warning);
    }
}
